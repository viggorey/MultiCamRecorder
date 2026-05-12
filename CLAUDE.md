# QueenPix — Hardware Trigger Debugging Session Notes

## Problem Statement
DMK23UP1300 cameras (USB3Vision) do not respond to function generator pulses when hardware trigger is ON.
- Trigger button ON → feed freezes ✓ (correct — camera enters trigger mode)
- Function generator at 5fps → **no frames appear** ✗
- Trigger button OFF → free-running live works fine ✓

## SDK / Camera Facts Confirmed

### VCD Property Tree for DMK23UP1300 Trigger
From the saved settings JSON (`C:\Users\viggo\AppData\Roaming\QueenPix\settings.json`) and SDK reflection:

| SDK Constant | GUID | Matches camera? |
|---|---|---|
| `VCDID_TriggerMode` | `90d57031-e43b-4366-aaeb-7a7a10b448b4` | ✓ (VCD XML item named "Trigger") |
| `VCDElement_Value` | `b57d3000-0ac6-4819-a609-272a33140aca` | ✓ (Enable element, Switch interface) |
| `VCDElement_TriggerPolarity` | `6519038d-1ad8-4e91-9021-66d64090cc85` | ✓ (Polarity element, Switch interface) |
| `VCDElement_TriggerMode` | `6519038e-1ad8-4e91-9021-66d64090cc85` | ✗ NOT in this camera's VCD XML |
| `VCDElement_TriggerDelay` | `c337cfb8-ea08-4e69-a655-586937b6afec` | ✓ (Delay = 15.0, AbsoluteValue) |

**The DMK23UP1300 trigger property has only 3 elements: Enable (Switch), Polarity (Switch), Delay (AbsoluteValue). There is NO TriggerMode sub-element (no hardware vs software mode selection).**

### Polarity
- Polarity is a **VCDSwitchProperty** (boolean), NOT VCDMapStringsProperty
- Saved VCD XML has `value="0"` (false) for all cameras
- Interpretation: `false` = HighActive/RisingEdge, `true` = LowActive/FallingEdge (verify with TIS docs)
- Our old code searched for polarity as `VCDMapStringsProperty` → always returned null → polarity unchanged from XML

### VCD XML Saved State (from settings.json)
| Camera | Enable (trigger) | Polarity |
|---|---|---|
| DMK 23UP1300 (cam 0) | 1 (enabled) | 0 |
| DMK 23UP1300 1 (cam 1) | 0 (disabled) | 0 |
| DMK 23UP1300 2 (cam 2) | 0 (disabled) | 0 |
| DMK 23UP1300 3 (cam 3) | 1 (enabled) | 0 |

All cameras have `UseExternalTrigger = true` in settings.

### Critical SDK Behaviour (from SDK docs)
> "Available: Indicates whether this interface is currently available. **For example, interfaces for the trigger property item may not be available while the image stream is running.**"

This means: trigger VCD properties should be set **BEFORE** `LiveStart()`, not after.

## Changes Made (latest code state)

### 1. Trigger set BEFORE LiveStart (`BtnStartLive_Click`)
```csharp
// Set trigger BEFORE LiveStart — USB3Vision cameras require trigger
// mode to be configured before the stream starts, not after.
if (camera.Settings.UseExternalTrigger)
{
    bool ok = SetHardwareTrigger(camera.ImagingControl, true);
    camera.HardwareTriggerEnabled = ok;
}
camera.ImagingControl.LiveStart();
UpdateTriggerButtonAppearance(camera);
```

### 2. LiveStop → set trigger → LiveStart in `ToggleCameraTrigger`
```csharp
bool wasLive = camera.ImagingControl.LiveVideoRunning;
if (wasLive) camera.ImagingControl.LiveStop();
bool success = SetHardwareTrigger(camera.ImagingControl, newState);
if (wasLive) camera.ImagingControl.LiveStart();
```

### 3. Same fix applied in settings-change restart path (~line 5484)

### 4. Diagnostic readback added to `SetHardwareTrigger`
```csharp
LogCameraInfo($"Trigger switch Available={triggerSwitch.Available}, ReadOnly={triggerSwitch.ReadOnly}, current={triggerSwitch.Switch}");
triggerSwitch.Switch = enable;
LogCameraInfo($"Hardware trigger enable → {enable} (readback: {triggerSwitch.Switch})");
```

### 5. Polarity correctly found as VCDSwitchProperty (just logs value, doesn't override)
```csharp
var polaritySwitch = ic.VCDPropertyItems.Find<VCDSwitchProperty>(
    VCDGUIDs.VCDID_TriggerMode, VCDGUIDs.VCDElement_TriggerPolarity);
if (polaritySwitch != null)
    LogCameraInfo($"TriggerPolarity (Switch) = {polaritySwitch.Switch} (false=HighActive/RisingEdge, true=LowActive/FallingEdge)");
```

## NEXT STEP — CRITICAL: Run and check the log

The **build compiles cleanly** (0 errors). The user needs to:

1. Build and run the app (Debug x64)
2. Press **Start Live**
3. Turn on function generator at 5fps
4. Check **`C:\Users\viggo\Desktop\CameraRecording.log`**

### What to look for in the log:
```
Trigger switch Available=True/False ...
Hardware trigger enable → True (readback: True/False)
TriggerPolarity (Switch) = True/False
```

- If `Available=False` → trigger can't be set (SDK is blocking it)
- If `readback=False` → property is being silently rejected (camera won't accept it in this state)
- If `readback=True` but no frames → trigger is set correctly but camera isn't responding to signal (polarity mismatch or hardware/signal issue)

## Remaining Hypotheses (in order of likelihood)

1. **`Available=False` when called** — SDK blocks trigger changes in this camera state. Would need to find a different timing/approach.

2. **Polarity mismatch** — `Polarity=0` (false=HighActive) but function generator outputs falling-edge signal. Fix: set `polaritySwitch.Switch = true` in `SetHardwareTrigger`.

3. **`LiveStart()` resets trigger state** — USB3Vision stream start may re-apply camera defaults, overriding the VCD property set before LiveStart. Fix: set trigger AFTER LiveStart but find a way to make `Available=True` after streaming starts.

4. **FrameHandlerSink interference** — `FrameHandlerSink` in grab mode without a FrameReady handler registered might interfere with display of triggered frames. Note: SDK docs say stream splits to display AND sink, so this shouldn't matter. But worth testing: temporarily remove the FrameHandlerSink setup in `BtnStartLive_Click` and retry.

5. **Hardware/signal issue** — Function generator signal not reaching camera trigger input (wrong connector pin, voltage level, etc.)

## SDK Notes
- `ICImagingControl.DeviceTrigger` — legacy DirectShow API, **DO NOT USE** — throws "option is not available" for USB3Vision cameras like DMK23UP1300
- `FrameSnapSink.SnapSingle()` returns `IFrameQueueBuffer` — incompatible with `ImageBuffer` (which implements `IFrame`). Cannot be used as a drop-in replacement for screenshot code.
- `FrameExtensions.CreateBitmapWrap(IFrame)` — extension method on `IFrame` (which `ImageBuffer` implements)
- SDK docs: image stream splits to display path AND sink path when `LiveDisplay=true` — having a custom sink should NOT prevent display updates

## File Locations
- Main code: `Form1.cs` (~10,000 lines)
- Log file: `C:\Users\viggo\Desktop\CameraRecording.log`
- Saved settings: `C:\Users\viggo\AppData\Roaming\QueenPix\settings.json`
- SetHardwareTrigger: ~line 5254
- ToggleCameraTrigger: ~line 5202
- BtnStartLive_Click: ~line 6282
- ApplyCameraSettings: ~line 5569
