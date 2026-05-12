using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

public class PairedBluetoothDevice
{
    public string MacKey;
    public string DeviceName;
    public bool IsConnected;
    public DateTime LastSeenUtc;
}

public class BluetoothDevicePairingManager : IDisposable
{
    private readonly object syncRoot = new object();
    private readonly Dictionary<string, PairedBluetoothDevice> devicesByMac = new Dictionary<string, PairedBluetoothDevice>(StringComparer.OrdinalIgnoreCase);
    private Timer scanTimer;
    private bool disposed;
    private bool scanInProgress;
    private string mainDeviceName;

    public event Action<string> StatusMessage;
    public event Action<List<PairedBluetoothDevice>> PairingStateChanged;

    public BluetoothDevicePairingManager()
    {
        mainDeviceName = Environment.MachineName;
    }

    public string MainDeviceName
    {
        get { return mainDeviceName; }
    }

    public void Start()
    {
        scanTimer = new Timer(ScanTick, null, 0, 8000);
    }

    private void ScanTick(object state)
    {
        if (disposed)
        {
            return;
        }

        lock (syncRoot)
        {
            if (scanInProgress)
            {
                return;
            }
            scanInProgress = true;
        }

        try
        {
            List<ScannedDevice> scannedDevices = ScanDevicesUsingLabScript();
            UpdatePairingStates(scannedDevices);
        }
        catch
        {
        }
        finally
        {
            lock (syncRoot)
            {
                scanInProgress = false;
            }
        }
    }

    private List<ScannedDevice> ScanDevicesUsingLabScript()
    {
        List<ScannedDevice> result = new List<ScannedDevice>();
        string scriptPath = GetBleakScriptPath();
        if (string.IsNullOrEmpty(scriptPath))
        {
            return result;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = "python";
        startInfo.Arguments = "\"" + scriptPath + "\"";
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        using (Process process = new Process())
        {
            process.StartInfo = startInfo;
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(12000);

            if (!string.IsNullOrWhiteSpace(output))
            {
                ParseScannerOutput(output, result);
            }
        }

        return result;
    }

    private string GetBleakScriptPath()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HCI Lab codes", "Bluetooth codes", "Bleak Bluetooth.py");
        if (File.Exists(path))
        {
            return path;
        }

        string rootPath = AppDomain.CurrentDomain.BaseDirectory;
        int index = 0;
        while (index < 4)
        {
            rootPath = Path.GetDirectoryName(rootPath);
            if (string.IsNullOrEmpty(rootPath))
            {
                break;
            }
            path = Path.Combine(rootPath, "HCI Lab codes", "Bluetooth codes", "Bleak Bluetooth.py");
            if (File.Exists(path))
            {
                return path;
            }
            index++;
        }

        return string.Empty;
    }

    private void ParseScannerOutput(string output, List<ScannedDevice> devices)
    {
        string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string currentName = "";
        int index = 0;
        while (index < lines.Length)
        {
            string line = lines[index];
            index++;
            if (line == null)
            {
                continue;
            }

            string trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("Device Name:", StringComparison.OrdinalIgnoreCase))
            {
                currentName = trimmedLine.Substring("Device Name:".Length).Trim();
                continue;
            }
            if (trimmedLine.StartsWith("MAC Address:", StringComparison.OrdinalIgnoreCase))
            {
                string macRaw = trimmedLine.Substring("MAC Address:".Length).Trim();
                ScannedDevice scanned = new ScannedDevice();
                scanned.DeviceName = currentName;
                scanned.MacRaw = macRaw;
                devices.Add(scanned);
                currentName = "";
                continue;
            }
        }
    }

    private string GetDeviceKey(string macRaw)
    {
        if (string.IsNullOrWhiteSpace(macRaw))
        {
            return string.Empty;
        }

        string normalized = NormalizeMac(macRaw);
        if (!string.IsNullOrEmpty(normalized))
        {
            return normalized;
        }

        return macRaw.Trim().ToUpperInvariant();
    }

    private void UpdatePairingStates(List<ScannedDevice> scannedDevices)
    {
        Dictionary<string, ScannedDevice> scannedByKey = new Dictionary<string, ScannedDevice>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        while (index < scannedDevices.Count)
        {
            ScannedDevice scanned = scannedDevices[index];
            index++;
            string key = GetDeviceKey(scanned.MacRaw);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }
            if (!scannedByKey.ContainsKey(key))
            {
                scannedByKey.Add(key, scanned);
            }
        }

        List<string> justConnectedLabels = new List<string>();
        List<string> justDisconnectedLabels = new List<string>();

        lock (syncRoot)
        {
            foreach (KeyValuePair<string, ScannedDevice> entry in scannedByKey)
            {
                string key = entry.Key;
                ScannedDevice scanned = entry.Value;
                string displayName = ResolveDisplayName(key, scanned);

                if (!devicesByMac.ContainsKey(key))
                {
                    PairedBluetoothDevice created = new PairedBluetoothDevice();
                    created.MacKey = key;
                    created.DeviceName = displayName;
                    created.IsConnected = true;
                    created.LastSeenUtc = DateTime.UtcNow;
                    devicesByMac.Add(key, created);
                    justConnectedLabels.Add(displayName);
                }
                else
                {
                    PairedBluetoothDevice existing = devicesByMac[key];
                    existing.DeviceName = displayName;
                    existing.LastSeenUtc = DateTime.UtcNow;
                    if (!existing.IsConnected)
                    {
                        existing.IsConnected = true;
                        justConnectedLabels.Add(displayName);
                    }
                }
            }

            List<string> keysToCheck = new List<string>(devicesByMac.Keys);
            int checkIndex = 0;
            while (checkIndex < keysToCheck.Count)
            {
                string key = keysToCheck[checkIndex];
                checkIndex++;
                PairedBluetoothDevice existing = devicesByMac[key];
                if (!scannedByKey.ContainsKey(key))
                {
                    if (existing.IsConnected)
                    {
                        existing.IsConnected = false;
                        justDisconnectedLabels.Add(existing.DeviceName);
                    }
                }
            }
        }

        int c = 0;
        while (c < justConnectedLabels.Count)
        {
            string label = justConnectedLabels[c];
            c++;
            RaiseStatusMessage(label + " connected");
        }

        int d = 0;
        while (d < justDisconnectedLabels.Count)
        {
            string label = justDisconnectedLabels[d];
            d++;
            RaiseStatusMessage(label + " disconnected");
        }

        RaisePairingStateChanged();
    }

    private string ResolveDisplayName(string macKey, ScannedDevice scanned)
    {
        if (!string.IsNullOrWhiteSpace(scanned.DeviceName))
        {
            string trimmed = scanned.DeviceName.Trim();
            if (trimmed.Length > 0 && trimmed.ToLowerInvariant() != "unknown")
            {
                return trimmed;
            }
        }
        // Names are usually missing from BLE advertisements. Keep devices
        // visually distinct by tagging them with the last 4 hex of the MAC.
        if (!string.IsNullOrEmpty(macKey))
        {
            int len = macKey.Length;
            string tail = len >= 4 ? macKey.Substring(len - 4) : macKey;
            return "BLE Tag " + tail;
        }
        return "Unknown Device";
    }

    private string FormatMacForDisplay(string macKey)
    {
        if (string.IsNullOrEmpty(macKey))
        {
            return "Unknown device";
        }

        if (macKey.Length == 12)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < 12)
            {
                if (i > 0)
                {
                    sb.Append(':');
                }
                sb.Append(macKey.Substring(i, 2));
                i = i + 2;
            }
            return sb.ToString();
        }

        return macKey;
    }

    private string NormalizeMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder();
        int index = 0;
        while (index < mac.Length)
        {
            char c = mac[index];
            index++;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        string normalized = sb.ToString();
        if (normalized.Length != 12)
        {
            return string.Empty;
        }

        return normalized;
    }

    public List<PairedBluetoothDevice> GetSnapshot()
    {
        List<PairedBluetoothDevice> snapshot = new List<PairedBluetoothDevice>();
        lock (syncRoot)
        {
            foreach (KeyValuePair<string, PairedBluetoothDevice> entry in devicesByMac)
            {
                PairedBluetoothDevice source = entry.Value;
                PairedBluetoothDevice clone = new PairedBluetoothDevice();
                clone.MacKey = source.MacKey;
                clone.DeviceName = source.DeviceName;
                clone.IsConnected = source.IsConnected;
                clone.LastSeenUtc = source.LastSeenUtc;
                snapshot.Add(clone);
            }
        }

        snapshot.Sort(CompareByNameThenMac);
        return snapshot;
    }

    private static int CompareByNameThenMac(PairedBluetoothDevice a, PairedBluetoothDevice b)
    {
        string nameA = a.DeviceName;
        string nameB = b.DeviceName;
        if (nameA == null)
        {
            nameA = "";
        }
        if (nameB == null)
        {
            nameB = "";
        }
        int byName = string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        if (byName != 0)
        {
            return byName;
        }
        string macA = a.MacKey;
        string macB = b.MacKey;
        if (macA == null)
        {
            macA = "";
        }
        if (macB == null)
        {
            macB = "";
        }
        return string.Compare(macA, macB, StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseStatusMessage(string message)
    {
        Action<string> handler = StatusMessage;
        if (handler != null)
        {
            handler(message);
        }
    }

    private void RaisePairingStateChanged()
    {
        Action<List<PairedBluetoothDevice>> handler = PairingStateChanged;
        if (handler == null)
        {
            return;
        }

        List<PairedBluetoothDevice> snapshot = GetSnapshot();
        handler(snapshot);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        if (scanTimer != null)
        {
            scanTimer.Dispose();
            scanTimer = null;
        }
    }

    private class ScannedDevice
    {
        public string DeviceName;
        public string MacRaw;
    }
}
