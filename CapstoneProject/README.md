# capstoneProject

## Overview
capstoneProject is a C# .NET 8 console application designed to automate documentation synchronization between a GitHub repository and Confluence.

The application reads a Confluence template page, scans repository files, extracts verified project facts, and generates a new technical profile page in Confluence.

## Features
- Reads template structure from Confluence
- Scans GitHub repository files recursively
- Extracts facts from configuration and documentation files
- Uses strict fact extraction rules
- Fills missing values with `Not Found` or `Not Specified`
- Creates a new Confluence page automatically

## Technology Stack
- **Language:** C#
- **Framework:** .NET 8
- **Type:** Console Application

## Input Sources
- **Confluence Template Page**: Defines the required documentation structure
- **GitHub Repository**: Provides application metadata and technical details

## Output
A new Confluence page titled:

`[App Name] - Technical Profile (Auto-Generated)`

## Planned Configuration
- Confluence base URL
- Confluence space key
- Template page title
- GitHub repository URL
- GitHub branch

## Project Structure
- `Program.cs` - Application entry point
- `appsettings.json` - Configuration values
- `README.md` - Project documentation
- `.gitignore` - Files excluded from source control
- `CapstoneProject.csproj` - Project definition

## Notes
This project is intended for automated technical documentation generation with strict data validation and no guessing of missing information.