# Attener

A single-user, offline-first Windows desktop app for tracking class attendance by scheduled hours.

## Technology

- C# and .NET 10
- WPF desktop interface
- SQLite local database

## Current features

- Add and archive subjects while preserving their attendance history.
- Build a weekly timetable on an hourly grid; adjacent hours for the same subject become one class.
- Choose the hours the grid shows with a clock picker; classes outside them are cropped from today onward.
- Edit the current weekly timetable; a change applies from today onward and earlier days are never touched.
- Earlier timetables can be edited too: each period in which the timetable was the same is listed with its dates, and a change there only affects those dates.
- Editing a timetable never touches marked attendance. Unmarked records in the edited dates are replaced to match the new classes, and any that are missing are created up to today.
- Select a calendar date to see each class time and its attendance status, and to mark or change it.
- Select the semester start date and generate historical Not Marked records. Moving it later deletes the earlier records after a confirmation.
- The Attendance page shows today's classes in a highlighted card at the top, followed by one card for each earlier date that still has unmarked classes. Each class counts its full scheduled duration.
- Add attendance by hand for any date (past, today or future): pick a subject, status and duration. Hand-added entries can be deleted.
- View subject-wise attendance statistics.
- Permanently delete a subject, or all data, from the app.

## Local data

The database is stored at `%LOCALAPPDATA%\Attener\attendance.db`.

## Build

Open `src/Attener/Attener.csproj` in Visual Studio or build it with the .NET 10 SDK.

## Publish a Windows executable

Use the `Windows-x64` publish profile in `src/Attener/Properties/PublishProfiles`. It creates `Attener.exe`, a self-contained single-file Windows x64 executable in `src/Attener/bin/Release/net10.0-windows/win-x64/publish/`.





