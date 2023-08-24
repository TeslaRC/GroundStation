# Ground Station for 4G FPV RC Car

This repository contains the source code and information for the TeslaRC Ground Station, which is used to control an RC FPV car equipped with high-definition video streaming, 2-way RC control signals, and more.

## Table of Contents

- [Introduction](#introduction)
- [Features](#features)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Installation](#installation)
- [Usage](#usage)
- [License](#license)
- [Acknowledgements](#acknowledgements)

## Introduction

TeslaRC Ground Station is designed to provide a comprehensive control interface for remote controlling an FPV car equipped with high-definition video streaming capabilities. The application supports Logitech G27 steering wheels for intuitive control and provides real-time video feedback from the car's perspective.

## Features

- Real-time high-definition video streaming from the FPV car's camera (below 120ms).
- Support for Logitech steering wheels for precise control.
- Keyboard support for basic control functions.
- User-friendly graphical interface for easy interaction.
- Simulated gearbox for immerse

## Getting Started

### Prerequisites

- Logitech G27 steering wheel (optional but recommended for enhanced control)
- .NET Framework (version 3.5 or higher)
- GStreamer libraries (included into this repository)
- A TeslaRC compatible FPV car equipped with a camera and control system

### Installation

1. Clone this repository to your local machine using the following command:
`git clone https://github.com/your-username/TeslaRC-Ground-Station.git`

2. Install the required GStreamer libraries and .NET Framework on your machine.

3. Open the `TeslaRC.sln` solution file in Visual Studio.

4. Build the solution to compile the application. (remember, move `LogitechSteeringWheelEnginesWrapper.dll` from lib/ to working directory of your app (that's where your exe is))

5. Connect your Logitech G27 steering wheel (if available) to your computer.

6. Connect the FPV car's camera and control system.

## Usage

1. Launch the TeslaRC application after connecting your control device and ensuring the FPV car's camera is operational.

2. The application's main window will provide video streaming from the car's camera along with the car control interface.

3. Use the Logitech steering wheel to control the car's steering and throttle. Alternatively, you can use keyboard shortcuts for basic control functions.

4. Enjoy remote controlling your FPV car with real-time video feedback!

## License

This project is licensed under the **GNU GPL** license. You are free to use, modify, and distribute this software according to the terms of the license.

## Acknowledgements

- The TeslaRC Ground Station software is developed by [tesla](https://github.com/tesla15).
- Special thanks to contributors and the open-source community for their valuable contributions and support.

---

