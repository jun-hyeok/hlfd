# hlfd

MS Hololens2 Based Face Detection and Keypoint Mapping

## Overview

This project, developed by Junhyeok Park(@jun-hyeok) at the Korea Advanced Institute of Science and Technology (KAIST), aims to create an AR UI/UX platform for smart glasses. The platform is designed to adapt to user context and individual characteristics.

The project focuses on three core technologies:

1. Dynamic interfaces based on user attention and intention
2. Input recognition and object control modules
3. Global position estimation for outdoor users

These technologies are integrated into a WISE AR UI/UX platform for smart glasses, with potential applications in education, defense, travel, and construction.

## Key Features

### 1. Real-time PV Stream on Hololens2
The system collects and preprocesses video streams from the smart glasses camera.

### 2. Face Detection and Keypoints Extraction with Sentis
The project uses a Unity Sentis model for face detection and keypoints extraction.

### 3. Position Bounding Boxes and Keypoints Mapping
The system uses detected face and keypoint data to develop social interaction features in AR environments.

## System Description

The face detection system is integrated with the BlazeFace model in a Unity environment, using Unity Sentis for real-time face detection and facial feature extraction. The process is asynchronous and uses GPU for parallel execution.

Key aspects of the system include:

- Support for both HoloLens2 and PC webcams
- Real-time face detection using the BlazeFace model
- Scaling of input tensors to match the image coordinate system
- Use of Non-Maximum Suppression (NMS) to filter overlapping detections
- Real-time visualization of detected faces and keypoints using Unity's GPU

## Applications and Advantages

This system enhances social interaction in AR/VR environments by providing real-time face detection and keypoint extraction on smart glasses. It supports personalized interfaces and AR social functions.

## Usage Instructions

To use the system:

1. Build the project in Unity as UWP (ARM64) and deploy it to HoloLens 2:
   - `File` > `Build Settings` > `Platform: Universal Windows Platform`
   - `Target Device: HoloLens` > `Architecture: ARM64` > `Build and Run`
   - Enter HoloLens 2 IP address and login information

2. Run the program to verify real-time camera-based face detection and keypoint mapping.

## Acknowledgments

This work was supported by the Institute of Information & communications Technology Planning & Evaluation (IITP) grant funded by the Korea government (MSIT) (No.2019-0-01270, WISE AR UI/UX Platform Development for Smartglasses).
