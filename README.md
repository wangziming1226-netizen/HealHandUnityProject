# HealHandUnityProject

Gesture-based hand rehabilitation training game for iPad, built with Unity and MediaPipe Hands.

The project provides a touchless interaction experience: users control the system only with hand gestures in front of the camera. It currently supports two training modes:

- **Card Mode** – scan physical QR cards to trigger predefined gesture exercises in a fixed sequence.  
- **Random Mode** – the system automatically selects gesture tasks with different difficulty levels to keep training varied and engaging.

The goal is to offer a low-cost, privacy-friendly rehabilitation tool that can be used at home or in clinical settings, while logging basic performance data for later review.

---

## Requirements

- Unity 6.x (URP template) or later  
- MediaPipe Hands (Unity plugin or integration)  
- macOS + Xcode (for iOS / iPadOS builds)  
- Target device: iPad with a front-facing camera

---

## Getting Started

1. **Clone the repository**

   ```bash
   git clone https://github.com/wangziming1226-netizen/HealHandUnityProject.git
   cd HealHandUnityProject
Open in Unity

2. **Open Unity Hub**

Click Add and select the HealHandUnityProject folder

Open the project and load the main scene (e.g. from Assets/_MyGameScenes)

3. **Run in the editor**

Make sure your camera permissions are enabled

Press Play in the Unity editor

Use your hand in front of the camera to interact with the UI and try Card Mode / Random Mode

4. **Build for iPad (optional)**

In Unity: File → Build Settings…

Select iOS, click Switch Platform

Add the main scene to Scenes In Build

Click Build, then open the generated Xcode project and deploy to an iPad
