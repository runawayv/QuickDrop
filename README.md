# QuickDrop

> **Fast file transfer between your PC and phone over the local network.**
> No cloud services, cables, or registration required.

<p align="center">
  <img src="https://img.shields.io/badge/C%23-.NET%2010-blue?style=for-the-badge&logo=csharp" alt="C#">
  <img src="https://img.shields.io/badge/WPF-Desktop-purple?style=for-the-badge" alt="WPF">
  <img src="https://img.shields.io/badge/Windows-11-blue?style=for-the-badge&logo=windows" alt="Windows">
  <img src="https://img.shields.io/github/license/runawayv/QuickDrop?style=for-the-badge" alt="License">
</p>

---
## 🗺️ Roadmap

### 🏁 Core & Current State
- [x] Basic file transfer core engine implemented in C# / .NET
- [x] Local network transfer support (via shared Wi-Fi router / LAN)
- [ ] 🟡 Automatic peer discovery (mDNS / UDP Broadcast)

### 🚀 Next Milestones (High Priority)
- [ ] **Wi-Fi Direct Integration** (True P2P connection between PC and Mobile without a router)
- [x] Clipboard synchronization (Seamless text and link sharing)

### 🔒 Security & UX
- [ ] End-to-End Encryption (TLS/SSL) for secure local data streaming
- [ ] System tray integration and background operation mode

### 📱 Ecosystem Expansion
- [ ] Native Android client application
- [ ] Native iOS/macOS client application


## What is QuickDrop?

**QuickDrop** is a lightweight Windows application for transferring files between your PC and phone over **Wi-Fi, Ethernet, or a mobile hotspot**.

No accounts, cloud services, or cables.

Simply:

**Launch → Scan the QR code → Transfer files.**

Your phone opens a regular web page in the browser, so there is no need to install anything on your phone.

---

## Features

* 📁 **Drag & Drop** — drag files and folders directly into the app
* 📱 **Upload from phone** — send files through your browser
* 📥 **Download to phone** — access and download files from your PC
* 📷 **QR Code** — connect instantly without entering an IP address
* 🔗 **Local Link** — connect using a local network address
* 📶 **Wi-Fi / Ethernet / Hotspot** — works over your local network
* 🚫 **No Internet Required** — works completely offline
* 📊 **Transfer Progress** — speed, progress, and status
* 🕘 **Transfer History**
* 🌙 **Dark & Light Theme**
* 🌍 **English / Русский**
* 🔔 **System Tray Icon**
* ⚡ **Windows Startup**

---

## How does it work?

```text
             Local Network
                  |
        +---------+---------+
        |                   |
     PC                  Phone
        |                   |
   QuickDrop            Browser
        |                   |
        +------- ↔ ---------+
```

QuickDrop starts a local web server on your PC.

Your phone connects to it through the local network and opens a web interface for transferring files.

**Files are not uploaded to the Internet or sent through third-party servers.**

---

## Installation

### 1. Install .NET

Install **.NET Desktop Runtime 10 (x64)**:

[.NET 10 Downloads](https://dotnet.microsoft.com/download/dotnet/10.0?utm_source=chatgpt.com)

> If the required Runtime is already installed, you can skip this step.

### 2. Download QuickDrop

Download the latest version from **Releases**:

[QuickDrop Releases](https://github.com/runawayv/QuickDrop/releases?utm_source=chatgpt.com)

Extract the archive anywhere on your PC.

### 3. Launch

Run:

```text
QuickDrop.exe
```

Scan the QR code with your phone's camera.

That's it — you can start transferring files.

> ⚠️ Keep the `www` folder next to `QuickDrop.exe`. It contains the web interface used by your phone.

---

## Build from Source

### Requirements

* Windows 10 / 11
* Visual Studio 2022+
* **.NET desktop development** workload
* .NET 10 SDK

### Clone the repository

```bash
git clone https://github.com/runawayv/QuickDrop.git
cd QuickDrop
```

Open:

```text
QuickDrop.sln
```

Select the `QuickDrop` project and press:

```text
F5
```

---

## Technologies

| Technology          | Purpose                   |
| ------------------- | ------------------------- |
| **C#**              | Main programming language |
| **.NET 10**         | Framework                 |
| **WPF**             | Windows desktop interface |
| **QRCoder**         | QR code generation        |
| **HTTP**            | File transfer             |
| **HTML / CSS / JS** | Phone web interface       |

---

## Privacy

QuickDrop is designed to work **entirely within your local network**.

Files are transferred directly:

```text
PC <──────────> Phone
```

instead of through a cloud server:

```text
PC ──> Cloud Server ──> Phone
```

**No Internet connection is required.**

---

## Screenshots

> Screenshots of the interface will be added soon.

---

## License

This project is licensed under the **MIT License**.

See [`LICENSE`](LICENSE).

---

<p align="center">
  Made with ❤️ and C# by <a href="https://github.com/runawayv">runawayv</a>
</p>
