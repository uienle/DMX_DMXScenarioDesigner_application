# DMX Scenario Designer

[English](#english) | [Tiếng Việt](#tiếng-việt)

## English

### Overview

DMX Scenario Designer is a Windows desktop application developed using C# and WPF. It provides a timeline-based interface for designing DMX lighting and motor-control scenarios.

The application communicates with the DMX Controller through USB serial communication to transfer scenarios for independent execution.

### Features

- Design control scenarios.
- Arrange and edit timeline items for supported lighting and motor-control effects.
- Integrate audio-related functionality into the scenario design workflow.
- Transfer scenarios to the DMX Controller through a USB serial connection.

### Technology Stack

- **Language:** C#
- **Framework:** .NET 8
- **Desktop UI:** Windows Presentation Foundation (WPF), XAML
- **Libraries:** NAudio, System.IO.Ports, System.Management

### Requirements

- Windows
- Visual Studio with the **.NET desktop development** workload
- .NET 8 SDK
- A compatible DMX Controller and USB serial connection for hardware testing

### Setup and Run

1. Clone or download this repository.
2. Open `DMXScenarioDesigner.sln` in Visual Studio.
3. Allow Visual Studio to restore the required NuGet packages.
4. Build the solution.
5. Run the application from Visual Studio.

To test hardware communication, connect the DMX Controller and configure the appropriate serial port.

### Related Repositories

- **DMX Controller (TX Firmware):** [STM32_TxDMX](https://github.com/uienle/STM32_TxDMX)
- **DMX Device Node (RX Firmware):** [STM32_RxDMX-LED_MOTOR](https://github.com/uienle/STM32_RxDMX-LED_MOTOR)

### Development Notes

This project was developed as part of a hands-on embedded DMX control system project, with assistance from Google AI Studio during development.

It provided practical experience in desktop application development, timeline-based UI design, audio-related functionality, and serial communication between a PC application and embedded hardware.

The source code is provided for learning and review. Further improvements to the audio-related functionality may be explored in future development.

### Demo

- [PC Application Demo](https://youtu.be/axNq71pUrME)
- [Full DMX Control System Demo](https://youtu.be/cifhSf1y9DU)

---

## Tiếng Việt

### Tổng quan

DMX Scenario Designer là ứng dụng desktop chạy trên Windows, được phát triển bằng C# và WPF. Ứng dụng cung cấp giao diện timeline để thiết kế kịch bản điều khiển đèn DMX và motor.

Ứng dụng giao tiếp với DMX Controller qua USB Serial để truyền kịch bản. Sau khi nhận được kịch bản, Controller có thể tự thực thi độc lập.

### Chức năng chính

- Thiết kế kịch bản điều khiển.
- Sắp xếp và chỉnh sửa các thành phần trên timeline cho những hiệu ứng đèn và motor được hỗ trợ.
- Tích hợp các chức năng liên quan đến âm thanh vào quy trình thiết kế kịch bản.
- Truyền kịch bản đến DMX Controller qua kết nối USB Serial.

### Công nghệ sử dụng

- **Ngôn ngữ:** C#
- **Framework:** .NET 8
- **Giao diện desktop:** Windows Presentation Foundation (WPF), XAML
- **Thư viện:** NAudio, System.IO.Ports, System.Management

### Yêu cầu

- Hệ điều hành Windows
- Visual Studio có workload **.NET desktop development**
- .NET 8 SDK
- DMX Controller tương thích và kết nối USB Serial nếu muốn thử nghiệm với phần cứng

### Cài đặt và chạy

1. Clone hoặc tải repository về máy.
2. Mở file `DMXScenarioDesigner.sln` bằng Visual Studio.
3. Chờ Visual Studio khôi phục các NuGet package cần thiết.
4. Build solution.
5. Chạy ứng dụng trực tiếp từ Visual Studio.

Để thử nghiệm giao tiếp phần cứng, hãy kết nối DMX Controller và cấu hình đúng Serial port.

### Các repository liên quan

- **DMX Controller (TX Firmware):** [STM32_TxDMX](https://github.com/uienle/STM32_TxDMX)
- **DMX Device Node (RX Firmware):** [STM32_RxDMX-LED_MOTOR](https://github.com/uienle/STM32_RxDMX-LED_MOTOR)

### Ghi chú phát triển

Dự án được phát triển trong quá trình thực hiện hệ thống điều khiển DMX, với sự hỗ trợ của Google AI Studio.

Quá trình này giúp tích lũy kinh nghiệm thực hành về phát triển ứng dụng desktop, thiết kế giao diện timeline, xử lý các chức năng liên quan đến âm thanh và giao tiếp Serial giữa ứng dụng PC với phần cứng nhúng.

Source code được chia sẻ nhằm phục vụ việc tìm hiểu và đánh giá. Các chức năng liên quan đến âm thanh có thể tiếp tục được cải thiện trong những phiên bản sau.

### Demo

- [Demo ứng dụng PC](https://youtu.be/axNq71pUrME)
- [Demo toàn bộ hệ thống DMX](https://youtu.be/cifhSf1y9DU)
