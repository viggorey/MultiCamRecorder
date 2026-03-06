using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace QueenPix
{
    /// <summary>
    /// COM interop helpers for enumerating DirectShow video capture devices.
    /// </summary>
    static class DirectShowHelper
    {
        // --- COM GUIDs ---
        private static readonly Guid CLSID_SystemDeviceEnum    = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        private static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");

        // --- COM interfaces (minimal, just what we need) ---
        [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum
        {
            [PreserveSig]
            int CreateClassEnumerator([In] ref Guid clsidDeviceClass, out IEnumMoniker ppEnumMoniker, int dwFlags);
        }

        [ComImport, Guid("00000102-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumMoniker
        {
            [PreserveSig]
            int Next(int celt, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IMoniker[] rgelt, IntPtr pceltFetched);
            [PreserveSig]
            int Skip(int celt);
            [PreserveSig]
            int Reset();
            void Clone(out IEnumMoniker ppenum);
        }

        [ComImport, Guid("0000000F-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMoniker
        {
            void BindToObject(IntPtr pbc, IMoniker? pmkToLeft, [In] ref Guid riidResult, [MarshalAs(UnmanagedType.IUnknown)] out object ppvResult);
            void BindToStorage(IntPtr pbc, IMoniker? pmkToLeft, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObj);
            void Reduce(IntPtr pbc, int dwReduceHowFar, ref IMoniker? ppmkToLeft, out IMoniker ppmkReduced);
            void ComposeWith(IMoniker pmkRight, bool fOnlyIfNotGeneric, out IMoniker ppmkComposite);
            void Enum(bool fForward, out IEnumMoniker ppenumMoniker);
            void IsEqual(IMoniker pmkOtherMoniker);
            void Hash(out int pdwHash);
            void IsRunning(IntPtr pbc, IMoniker? pmkToLeft, IMoniker? pmkNewlyRunning);
            void GetTimeOfLastChange(IntPtr pbc, IMoniker? pmkToLeft, out System.Runtime.InteropServices.ComTypes.FILETIME pFileTime);
            void Inverse(out IMoniker ppmk);
            void CommonPrefixWith(IMoniker pmkOther, out IMoniker ppmkPrefix);
            void RelativePathTo(IMoniker pmkOther, out IMoniker ppmkRelPath);
            void GetDisplayName(IntPtr pbc, IMoniker? pmkToLeft, [MarshalAs(UnmanagedType.LPWStr)] out string ppszDisplayName);
            void ParseDisplayName(IntPtr pbc, IMoniker pmkToLeft, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out int pchEaten, out IMoniker ppmkOut);
            void IsSystemMoniker(out int pdwMksys);
        }

        [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig]
            int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar, IntPtr pErrorLog);
            [PreserveSig]
            int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
        }

        /// <summary>
        /// Returns all DirectShow video input devices as (FriendlyName, DeviceIndex).
        /// DeviceIndex is the 0-based index suitable for use with OpenCV VideoCapture.
        /// </summary>
        public static List<(string Name, int Index)> EnumerateVideoDevices()
        {
            var result = new List<(string Name, int Index)>();
            try
            {
                var sysDevEnumType = Type.GetTypeFromCLSID(CLSID_SystemDeviceEnum)!;
                var devEnum = (ICreateDevEnum)Activator.CreateInstance(sysDevEnumType)!;

                Guid category = CLSID_VideoInputDeviceCategory;
                int hr = devEnum.CreateClassEnumerator(ref category, out IEnumMoniker enumMoniker, 0);
                if (hr != 0 || enumMoniker == null)
                    return result;

                var monikers = new IMoniker[1];
                int idx = 0;
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    try
                    {
                        Guid bagGuid = typeof(IPropertyBag).GUID;
                        monikers[0].BindToStorage(IntPtr.Zero, null, ref bagGuid, out object bagObj);
                        var bag = (IPropertyBag)bagObj;

                        object nameVar = "";
                        bag.Read("FriendlyName", ref nameVar, IntPtr.Zero);
                        string name = nameVar?.ToString() ?? $"Camera {idx}";
                        result.Add((name, idx));
                    }
                    catch { result.Add(($"Camera {idx}", idx)); }
                    finally { if (monikers[0] != null) Marshal.ReleaseComObject(monikers[0]); }
                    idx++;
                }
                Marshal.ReleaseComObject(enumMoniker);
                Marshal.ReleaseComObject(devEnum);
            }
            catch { }
            return result;
        }

        // Standard resolutions to probe
        private static readonly (int W, int H)[] ProbeResolutions =
        {
            (640, 480),
            (1280, 720),
            (1920, 1080),
            (2560, 1440),
        };

        /// <summary>
        /// Probes a DirectShow device to find which standard resolutions it actually accepts.
        /// Opens the device briefly with VideoCapture, sets Width/Height, and checks what was granted.
        /// </summary>
        public static List<(int Width, int Height)> GetSupportedResolutions(int deviceIndex)
        {
            var supported = new List<(int Width, int Height)>();
            try
            {
                using var cap = new VideoCapture(deviceIndex, (VideoCaptureAPIs)700);
                if (!cap.IsOpened())
                    return supported;

                // Record default resolution
                int defaultW = (int)cap.Get(VideoCaptureProperties.FrameWidth);
                int defaultH = (int)cap.Get(VideoCaptureProperties.FrameHeight);

                foreach (var (w, h) in ProbeResolutions)
                {
                    cap.Set(VideoCaptureProperties.FrameWidth, w);
                    cap.Set(VideoCaptureProperties.FrameHeight, h);
                    int actualW = (int)cap.Get(VideoCaptureProperties.FrameWidth);
                    int actualH = (int)cap.Get(VideoCaptureProperties.FrameHeight);
                    if (actualW == w && actualH == h)
                        supported.Add((w, h));
                }

                // Restore default
                cap.Set(VideoCaptureProperties.FrameWidth, defaultW);
                cap.Set(VideoCaptureProperties.FrameHeight, defaultH);

                // Always include at least 640x480
                if (supported.Count == 0)
                    supported.Add((640, 480));
            }
            catch { supported.Add((640, 480)); }
            return supported;
        }
    }
}
