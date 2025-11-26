# GitHub Setup Guide

This guide will help you upload your MultiCamRecorder project to GitHub.

## Prerequisites

You need Git installed on your computer. Choose one option:

### Option 1: Install Git (Command Line)
1. Download Git from: https://git-scm.com/download/win
2. Install with default settings
3. Restart your terminal/command prompt

### Option 2: Use GitHub Desktop (Easier)
1. Download GitHub Desktop from: https://desktop.github.com/
2. Sign in with your GitHub account
3. Skip to "Using GitHub Desktop" section below

---

## Method 1: Using Command Line (Git)

### Step 1: Create a GitHub Repository

1. Go to https://github.com and sign in
2. Click the "+" icon in the top right → "New repository"
3. Name it: `MultiCamRecorder` (or any name you prefer)
4. **DO NOT** initialize with README, .gitignore, or license (we already have these)
5. Click "Create repository"

### Step 2: Initialize Git and Push

Open PowerShell or Command Prompt in your project folder and run:

```bash
# Initialize git repository
git init

# Add all files (respects .gitignore)
git add .

# Create initial commit
git commit -m "Initial commit: MultiCamRecorder application"

# Add your GitHub repository as remote (replace YOUR_USERNAME with your GitHub username)
git remote add origin https://github.com/viggorey/MultiCamRecorder.git

# Push to GitHub
git branch -M main
git push -u origin main
```

**Note**: You'll be prompted for your GitHub username and password (or personal access token).

---

## Method 2: Using GitHub Desktop (Easier)

### Step 1: Create a GitHub Repository

1. Go to https://github.com and sign in
2. Click the "+" icon in the top right → "New repository"
3. Name it: `MultiCamRecorder`
4. **DO NOT** initialize with README, .gitignore, or license
5. Click "Create repository"

### Step 2: Add Repository in GitHub Desktop

1. Open GitHub Desktop
2. Click "File" → "Add Local Repository"
3. Click "Choose..." and select your project folder: `C:\Users\vr382\Desktop\Projects\MultiCamRecorder\MultiCamRecorder`
4. Click "Add Repository"

### Step 3: Commit and Push

1. You'll see all your files listed as changes
2. At the bottom, enter a commit message: "Initial commit: MultiCamRecorder application"
3. Click "Commit to main"
4. Click "Publish repository" (or "Push origin" if already published)
5. Make sure "Keep this code private" is unchecked (unless you want a private repo)
6. Click "Publish Repository"

---

## What Gets Uploaded?

The `.gitignore` file I created will exclude:
- ✅ Build outputs (`bin/`, `obj/`)
- ✅ User-specific files (`.user`, `.suo`)
- ✅ Visual Studio cache (`.vs/`)
- ✅ **Sensitive files** (`Information` file with password)
- ✅ NuGet packages cache

**Files that WILL be uploaded:**
- ✅ All source code (`.cs` files)
- ✅ Project file (`.csproj`)
- ✅ README.md
- ✅ DEPLOYMENT_GUIDE.md
- ✅ .gitignore

---

## Downloading on Another Computer

Once uploaded, you can download it on another computer:

### Using Command Line:
```bash
git clone https://github.com/YOUR_USERNAME/MultiCamRecorder.git
cd MultiCamRecorder
```

### Using GitHub Desktop:
1. Open GitHub Desktop
2. Click "File" → "Clone Repository"
3. Select your repository
4. Choose a local path
5. Click "Clone"

### Then Build and Run:
```bash
cd MultiCamRecorder
dotnet restore
dotnet build
dotnet run
```

---

## Important Notes

1. **Password Security**: The `Information` file (which contains a password) is excluded by `.gitignore` and will NOT be uploaded. This is intentional for security.

2. **TIS Imaging Control DLL**: The project references TIS.Imaging.ICImagingControl35.dll from a specific path. On the other computer, you'll need to:
   - Install TIS Imaging Control 3.5
   - Or copy the DLL to the project folder and update the reference

3. **Settings**: User settings are stored in `%AppData%\MultiCamRecorder\settings.json` and won't be in the repository (this is good - each user has their own settings).

---

## Troubleshooting

### "Repository not found" error
- Make sure you've created the repository on GitHub first
- Check that the repository name matches exactly
- Verify your GitHub username is correct

### Authentication errors
- GitHub no longer accepts passwords for Git operations
- You need to use a Personal Access Token:
  1. Go to GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
  2. Generate a new token with `repo` permissions
  3. Use this token as your password when pushing

### Files not showing up
- Make sure you've run `git add .` to stage files
- Check that files aren't being ignored by `.gitignore`

---

## Next Steps

After uploading:
1. Test cloning on another computer to make sure everything works
2. Consider adding a LICENSE file
3. Update README.md with your contact information
4. Add any additional documentation you need

