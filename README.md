# Attener

A single-user, offline-first Windows desktop app for tracking class attendance by scheduled hours.

## Technology

- C# and .NET 10
- WPF desktop interface
- SQLite local database

## Current features

- Add and archive subjects while preserving their attendance history.
- Create regular weekly sessions with exact start and end times.
- Apply timetable changes from a chosen effective date while retaining earlier schedule history.
- Select a calendar date to see each class time and its attendance status.
- Select the semester start date and generate historical Not Marked records.
- Mark attendance by date or work from the pending list.
- Edit actual class duration in hours.
- View overall and subject-wise attendance statistics.

## Local data

The database is stored at `%LOCALAPPDATA%\AttendanceApp\attendance.db` so existing local data is preserved.

## Build

Open `src/AttendanceApp/AttendanceApp.csproj` in Visual Studio or build it with the .NET 10 SDK.

## Publish a Windows executable

Use the `Windows-x64` publish profile in `src/AttendanceApp/Properties/PublishProfiles`. It creates `Attener.exe`, a self-contained single-file Windows x64 executable in `src/AttendanceApp/bin/Release/net10.0-windows/win-x64/publish/`.

