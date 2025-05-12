# Eye-Controlled Piano Interface

## Overview
This application provides an accessible piano interface controlled entirely by eye gaze, designed to assist playing music together with neural-interface. By leveraging eye-tracking technology, the program allows users to choose MIDI notes by focusing their gaze on virtual piano keys. The interface includes a dynamically resizing keyboard and a large activation button, offering an inclusive and adaptive musical experience. The project demonstrates how assistive technologies can empower users by transforming gaze inputs into meaningful artistic expressions.

## Technical Implementation
The application is built using C# and the Universal Windows Platform (UWP), ensuring compatibility with Windows devices equipped with eye-tracking hardware. Key features include:
- **Gaze Tracking**: Utilizes `GazeInputSourcePreview` to capture real-time eye coordinates and map them to interactive piano keys.
- **Dynamic UI**: Implements a responsive keyboard that adjusts key sizes based on screen width, ensuring optimal usability across devices.
- **REST Integration**: Sends HTTP requests to a local server (e.g., `http://localhost:8080`) to trigger MIDI notes or external actions when keys are activated.

## Architecture Details
- **Key Management**: Piano keys are generated programmatically as `Rectangle` elements, stored in a `Dictionary<Rectangle, int>` to map each key to its MIDI note value.
- **State Tracking**: Uses `gazeStartTime` and `activationCooldown` to manage gaze dwell times and prevent unintended repeated triggers.
- **Adaptive Layout**: The "big key" at the bottom extends the keyboard's functionality, activated with a shorter delay (100ms) and styled to provide clear visual feedback. Z-indexing and coordinate normalization ensure accurate hit detection across overlapping elements.
