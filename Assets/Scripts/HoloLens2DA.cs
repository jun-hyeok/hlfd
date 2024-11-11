
using System.Collections.Generic;
using UnityEngine;

public class HoloLens2DA : MonoBehaviour
{
    public Texture colormap_texture;
    public Shader colormap_shader;
    public Shader sqrtmap_shader;
    public Shader grayscale_shader;

    private Texture2D tex_pv;
    public Texture2D texPv => tex_pv;

    private Material colormap_mat_ht;
    private Material colormap_mat_lt;
    private Material sqrtmap_mat;
    private Material grayscale_mat;

    private Dictionary<hl2da.SENSOR_ID, string> sensor_names;
    private Dictionary<hl2da.SENSOR_ID, int> last_framestamp;
    private hl2da.SENSOR_ID sync;
    private hl2da.pv_captureformat pvcf;
    private ulong utc_offset;

    private Dictionary<hl2da.SENSOR_ID, float[,]> rm_uv2xy = new Dictionary<hl2da.SENSOR_ID, float[,]>();
    private Dictionary<hl2da.SENSOR_ID, float[,]> rm_mapxy = new Dictionary<hl2da.SENSOR_ID, float[,]>();
    private Dictionary<hl2da.SENSOR_ID, float[]> rm_intrinsics = new Dictionary<hl2da.SENSOR_ID, float[]>();

    // Start is called before the first frame update
    void Start()
    {
        // PV format
        // List of supported resolutions and framerates at https://github.com/jdibenes/hl2ss/blob/main/etc/pv_configurations.txt
        // enable_mrc: Enable Mixed Reality Capture (holograms in video)
        // shared: Enable Shared Mode (for when other apps or modules are already using the PV camera, resolution and framerate parameters are ignored)
        pvcf = hl2da.user.CreateFormat_PV(760, 428, 30, false, false);

        // Additional PV capture settings
        // To change the focus mode, temporal denoising, white balance, exposure, scene mode, iso speed, or backlight compensation,
        // after calling Initialize(PV, buffer_size) and SetEnable(PV, true),
        // wait to receive at least one frame (from hl2da.framebuffer.GetFrame with hl2da.STATUS.OK),
        // then these settings can be changed freely as long as the stream is enabled.
        // Unlike PV format, these settings are not latched internally, so they will be lost if the stream is restarted (via SetEnable(PV, false) -> SetEnable(PV, true))
        // and they must be reapplied using the SetEnable(PV, true) -> Wait one frame procedure.

        // Synchronize to sensor (value not in hl2da.SENSOR_ID = no synchronization)
        sync = (hl2da.SENSOR_ID)(-1); // hl2da.SENSOR_ID.RM_DEPTH_LONGTHROW;

        Initialize_Dictionaries();

        hl2da.user.InitializeComponents();
        hl2da.user.OverrideWorldCoordinateSystem(); // Link Unity and plugin coordinate systems

        hl2da.user.SetFormat_PV(pvcf);


        utc_offset = hl2da.user.GetUTCOffset();

        Update_Calibration();

        hl2da.user.SetConstantFactorVLC_RM(); // Workaround for https://github.com/microsoft/HoloLens2ForCV/issues/134
        hl2da.user.BypassDepthLock_RM(true); // Allows simultaneous access to AHAT and longthrow depth

        // Test, not required
        hl2da.user.EX_SetInterfacePriority(hl2da.SENSOR_ID.RM_VLC_LEFTFRONT, hl2da.InterfacePriority.ABOVE_NORMAL);
        hl2da.user.EX_SetInterfacePriority(hl2da.SENSOR_ID.RM_VLC_LEFTFRONT, hl2da.InterfacePriority.ABOVE_NORMAL);
        int pv_prio = hl2da.user.EX_GetInterfacePriority(hl2da.SENSOR_ID.PV);

        hl2da.user.Initialize(hl2da.SENSOR_ID.PV, 15); // Buffer size limited by internal buffer - Maximum is 18 // 5, 15, 30, 60 Hz selectable depending on chosen resolution 

        hl2da.user.SetEnable(hl2da.SENSOR_ID.PV, true);

        tex_pv = new Texture2D((int)hl2da.converter.GetStride_PV(pvcf.width), pvcf.height, TextureFormat.BGRA32, false);

        colormap_mat_ht = new Material(colormap_shader);
        colormap_mat_lt = new Material(colormap_shader);
        sqrtmap_mat = new Material(sqrtmap_shader);
        grayscale_mat = new Material(grayscale_shader);

        colormap_mat_ht.SetTexture("_ColorMapTex", colormap_texture);
        colormap_mat_ht.SetFloat("_Lf", 0.0f / 65535.0f);
        colormap_mat_ht.SetFloat("_Rf", 1055.0f / 65535.0f);

        colormap_mat_lt.SetTexture("_ColorMapTex", colormap_texture);
        colormap_mat_lt.SetFloat("_Lf", 0.0f / 65535.0f);
        colormap_mat_lt.SetFloat("_Rf", 3000.0f / 65535.0f);

    }

    // Update is called once per frame
    void Update()
    {
        if ((sync < hl2da.SENSOR_ID.RM_VLC_LEFTFRONT) || (sync > hl2da.SENSOR_ID.EXTENDED_VIDEO))
        {
            Update_FFA();
        }
        else
        {
            Update_Sync();
        }
    }

    void Update_FFA()
    {
        for (hl2da.SENSOR_ID id = hl2da.SENSOR_ID.RM_VLC_LEFTFRONT; id <= hl2da.SENSOR_ID.EXTENDED_VIDEO; ++id)
        {
            // Get Frame
            // Negative index (-n): get nth most recent frame, may repeat or skip frames as necessary
            // Non-negative index (n>=0): get nth frame where 0 is the first frame ever since SetEnable(true), intended for sequential access (no skip or repeat)
            using (hl2da.framebuffer fb = hl2da.framebuffer.GetFrame(id, -1)) // Get most recent frame
            {
                // Check result
                // DISCARDED: requested frame is too old and has been removed from the internal buffer (cannot be recovered)
                // OK: got requested frame
                // WAIT: requested frame has not been captured yet (just wait, ideally 1/frame_rate seconds)
                if (fb.Status != hl2da.STATUS.OK) { continue; }

                Update_Sensor_Data(fb);
            }
        }
    }

    void Update_Sync()
    {
        ulong fb_ref_timestamp;
        using (hl2da.framebuffer fb_ref = hl2da.framebuffer.GetFrame(sync, -2)) // Use a small delay to allow receiving the optimal frame from the second stream
        {
            if (fb_ref.Status != hl2da.STATUS.OK) { return; }
            fb_ref_timestamp = fb_ref.Timestamp;
            Update_Sensor_Data(fb_ref);
        }

        for (hl2da.SENSOR_ID id = hl2da.SENSOR_ID.RM_VLC_LEFTFRONT; id <= hl2da.SENSOR_ID.EXTENDED_VIDEO; ++id)
        {
            if (id == sync) { continue; }

            // Associate frames
            // If no frame matches fb_ref_timestamp exactly then:
            //   hl2da.TIME_PREFERENCE.PAST:    select nearest frame with Timestamp < fb_ref_timestamp
            //   hl2da.TIME_PREFERENCE.NEAREST: select nearest frame, in case of a tie choose Timestamp > fb_ref_timestamp if tiebreak_right=true else choose Timestamp < fb_ref_timestamp
            //   hl2da.TIME_PREFERENCE.FUTURE:  select nearest frame with Timestamp > fb_ref_timestamp
            using (hl2da.framebuffer fb = hl2da.framebuffer.GetFrame(id, fb_ref_timestamp, hl2da.TIME_PREFERENCE.NEAREST, false))
            {
                if (fb.Status != hl2da.STATUS.OK) { continue; }
                Update_Sensor_Data(fb);
            }
        }
    }

    void Initialize_Dictionaries()
    {
        sensor_names = new Dictionary<hl2da.SENSOR_ID, string>();
        last_framestamp = new Dictionary<hl2da.SENSOR_ID, int>();

        for (hl2da.SENSOR_ID id = hl2da.SENSOR_ID.RM_VLC_LEFTFRONT; id <= hl2da.SENSOR_ID.EXTENDED_VIDEO; ++id)
        {
            sensor_names[id] = id.ToString();
            last_framestamp[id] = -1;
        }
    }

    string CentralPoints(hl2da.SENSOR_ID id)
    {
        float[,] image_points = new float[1, 2];
        float[,] camera_points = new float[1, 2];

        switch (id)
        {
            case hl2da.SENSOR_ID.RM_VLC_LEFTFRONT:
            case hl2da.SENSOR_ID.RM_VLC_LEFTLEFT:
            case hl2da.SENSOR_ID.RM_VLC_RIGHTFRONT:
            case hl2da.SENSOR_ID.RM_VLC_RIGHTRIGHT: image_points[0, 0] = 320.0f; image_points[0, 1] = 240.0f; break;
            case hl2da.SENSOR_ID.RM_DEPTH_AHAT: image_points[0, 0] = 256.0f; image_points[0, 1] = 256.0f; break;
            case hl2da.SENSOR_ID.RM_DEPTH_LONGTHROW: image_points[0, 0] = 160.0f; image_points[0, 1] = 144.0f; break;
            default: return "";
        }

        camera_points[0, 0] = 0.0f;
        camera_points[0, 1] = 0.0f;

        float[,] mpoint = hl2da.user.RM_MapImagePointToCameraUnitPlane(id, image_points); // get image center in camera space
        float[,] ppoint = hl2da.user.RM_MapCameraSpaceToImagePoint(id, camera_points); // get image principal point

        return string.Format(" c'=[{0}, {1}], c=[{2}, {3}]", mpoint[0, 0], mpoint[0, 1], ppoint[0, 0], ppoint[0, 1]);
    }

    string Calibration(hl2da.SENSOR_ID id)
    {
        switch (id)
        {
            case hl2da.SENSOR_ID.RM_VLC_LEFTFRONT:
            case hl2da.SENSOR_ID.RM_VLC_LEFTLEFT:
            case hl2da.SENSOR_ID.RM_VLC_RIGHTFRONT:
            case hl2da.SENSOR_ID.RM_VLC_RIGHTRIGHT:
            case hl2da.SENSOR_ID.RM_DEPTH_AHAT:
            case hl2da.SENSOR_ID.RM_DEPTH_LONGTHROW: break;
            default: return "";
        }

        hl2da.user.RM_GetIntrinsics(id, out float[,] uv2xy, out float[,] mapxy, out float[] k);

        rm_uv2xy[id] = uv2xy;
        rm_mapxy[id] = mapxy;
        rm_intrinsics[id] = k;

        return string.Format(" fx={0}, fy={1}, cx={2}, cy={3}", k[0], k[1], k[2], k[3]);
    }

    void Update_Calibration()
    {
        for (hl2da.SENSOR_ID id = hl2da.SENSOR_ID.RM_VLC_LEFTFRONT; id <= hl2da.SENSOR_ID.RM_IMU_GYROSCOPE; ++id)
        {
            float[,] extrinsics = hl2da.user.RM_GetExtrinsics(id);
            string text = sensor_names[id] + " Calibration: extrinsics=" + PoseToString(extrinsics) + CentralPoints(id) + Calibration(id);
        }
    }

    void Update_Sensor_Data(hl2da.framebuffer fb)
    {
        if (fb.Framestamp <= last_framestamp[fb.Id]) { return; } // Repeated frame, nothing to do...
        last_framestamp[fb.Id] = fb.Framestamp;

        switch (fb.Id)
        {
            case hl2da.SENSOR_ID.PV: Update_PV(fb); break;
        }
    }

    void Update_PV(hl2da.framebuffer fb)
    {
        // Load frame data into textures
        using (hl2da.converter fc = hl2da.converter.Convert(fb.Buffer(0), hl2da.converter.GetStride_PV(pvcf.width), pvcf.height, hl2da.IMT_Format.Nv12, hl2da.IMT_Format.Bgra8)) // PV images are NV12
        {
            tex_pv.LoadRawTextureData(fc.Buffer, fc.Length);
            tex_pv.Apply();
        }
    }

    string PoseToString(float[,] pose)
    {
        return string.Format("[[{0}, {1}, {2}, {3}], [{4}, {5}, {6}, {7}], [{8}, {9}, {10}, {11}], [{12}, {13}, {14}, {15}]]", pose[0, 0], pose[0, 1], pose[0, 2], pose[0, 3], pose[1, 0], pose[1, 1], pose[1, 2], pose[1, 3], pose[2, 0], pose[2, 1], pose[2, 2], pose[2, 3], pose[3, 0], pose[3, 1], pose[3, 2], pose[3, 3]);
    }
}
