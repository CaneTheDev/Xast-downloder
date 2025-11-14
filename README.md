# Xast Downloader

The fastest multi-threaded download manager built with .NET 9 and Avalonia UI.

## Features

- 🚀 Multi-threaded downloads (16/32/64/128 connections)
- ⏸️ Pause/Resume support
- 📊 Real-time progress tracking
- 🎯 Download queue management
- 🔄 Auto-retry on failure
- 🌐 Cross-platform (Windows, macOS, Linux)

## Tech Stack

- .NET 9
- Avalonia UI (cross-platform desktop)
- MVVM architecture

## Project Structure

```
src/
├── core/              # Download engine
│   ├── engine/        # Multi-threaded download logic
│   ├── models/        # Data models
│   ├── services/      # Business logic
│   └── utils/         # Helper functions
└── ui/                # Avalonia UI
    ├── Views/         # XAML views
    ├── ViewModels/    # MVVM view models
    └── Assets/        # Icons, styles
```

## Development

**Run with hot reload:**
```bash
dotnet watch run --project src/ui
```

**Build:**
```bash
dotnet build
```

**Test:**
```bash
dotnet test
```

## How It Works

Like aria2c but with a GUI:
1. Sends HEAD request to get file size
2. Splits file into chunks (based on connection count)
3. Downloads chunks in parallel using HTTP range requests
4. Merges chunks as they complete
5. Supports resume from last byte on interruption
