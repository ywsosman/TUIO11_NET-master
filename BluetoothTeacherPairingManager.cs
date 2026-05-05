using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

public class TeacherRecord
{
    public string TeacherId;
    public string TeacherName;
    public string MacAddress;
    public string Role;
}

public class PairedTeacherDevice
{
    public TeacherRecord Teacher;
    public string DeviceName;
    public bool IsConnected;
    public DateTime LastSeenUtc;
}

public class BluetoothTeacherPairingManager : IDisposable
{
    private readonly object syncRoot = new object();
    private readonly Dictionary<string, TeacherRecord> teachersByMac = new Dictionary<string, TeacherRecord>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PairedTeacherDevice> pairedByMac = new Dictionary<string, PairedTeacherDevice>(StringComparer.OrdinalIgnoreCase);
    private Timer scanTimer;
    private bool disposed;
    private bool scanInProgress;
    private string mainDeviceName;

    public event Action<string> StatusMessage;
    public event Action<List<PairedTeacherDevice>> PairingStateChanged;

    public BluetoothTeacherPairingManager()
    {
        mainDeviceName = Environment.MachineName;
    }

    public string MainDeviceName
    {
        get { return mainDeviceName; }
    }

    public void Start()
    {
        bool databaseLoaded = LoadTeacherDatabase();
        if (!databaseLoaded)
        {
            RaiseStatusMessage("failed to fetch : No database detected");
            return;
        }

        RaiseStatusMessage("database connection complete");
        scanTimer = new Timer(ScanTick, null, 0, 8000);
    }

    private bool LoadTeacherDatabase()
    {
        string dbPath = GetTeacherDatabasePath();
        if (string.IsNullOrEmpty(dbPath))
        {
            return false;
        }
        if (!File.Exists(dbPath))
        {
            return false;
        }

        string[] lines = File.ReadAllLines(dbPath);
        if (lines == null || lines.Length == 0)
        {
            return false;
        }

        lock (syncRoot)
        {
            teachersByMac.Clear();
            int lineIndex = 0;
            while (lineIndex < lines.Length)
            {
                string line = lines[lineIndex];
                lineIndex++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                if (line.TrimStart().StartsWith("#"))
                {
                    continue;
                }

                string[] parts = line.Split(',');
                if (parts.Length < 4)
                {
                    continue;
                }

                TeacherRecord teacher = new TeacherRecord();
                teacher.TeacherId = parts[0].Trim();
                teacher.TeacherName = parts[1].Trim();
                teacher.MacAddress = NormalizeMac(parts[2]);
                teacher.Role = parts[3].Trim();

                if (string.IsNullOrEmpty(teacher.MacAddress))
                {
                    continue;
                }
                if (!string.Equals(teacher.Role, "teacher", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!teachersByMac.ContainsKey(teacher.MacAddress))
                {
                    teachersByMac.Add(teacher.MacAddress, teacher);
                }
            }
        }

        return teachersByMac.Count > 0;
    }

    private string GetTeacherDatabasePath()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "teacher_registry.csv");
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

            path = Path.Combine(rootPath, "Config", "teacher_registry.csv");
            if (File.Exists(path))
            {
                return path;
            }
            index++;
        }

        return string.Empty;
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
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(12000);

            if (!string.IsNullOrWhiteSpace(error))
            {
                // keep behavior stable; scanner errors should not crash game loop
            }

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
                string mac = trimmedLine.Substring("MAC Address:".Length).Trim();
                string normalizedMac = NormalizeMac(mac);
                if (!string.IsNullOrEmpty(normalizedMac))
                {
                    ScannedDevice scanned = new ScannedDevice();
                    scanned.DeviceName = currentName;
                    scanned.MacAddress = normalizedMac;
                    devices.Add(scanned);
                }
                currentName = "";
                continue;
            }
        }
    }

    private void UpdatePairingStates(List<ScannedDevice> scannedDevices)
    {
        Dictionary<string, ScannedDevice> scannedByMac = new Dictionary<string, ScannedDevice>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        while (index < scannedDevices.Count)
        {
            ScannedDevice scanned = scannedDevices[index];
            index++;
            if (!scannedByMac.ContainsKey(scanned.MacAddress))
            {
                scannedByMac.Add(scanned.MacAddress, scanned);
            }
        }

        List<string> justConnectedTeachers = new List<string>();
        List<string> justDisconnectedTeachers = new List<string>();

        lock (syncRoot)
        {
            foreach (KeyValuePair<string, TeacherRecord> teacherEntry in teachersByMac)
            {
                string mac = teacherEntry.Key;
                TeacherRecord teacher = teacherEntry.Value;

                if (scannedByMac.ContainsKey(mac))
                {
                    ScannedDevice scanned = scannedByMac[mac];
                    if (!pairedByMac.ContainsKey(mac))
                    {
                        PairedTeacherDevice paired = new PairedTeacherDevice();
                        paired.Teacher = teacher;
                        paired.DeviceName = scanned.DeviceName;
                        paired.IsConnected = true;
                        paired.LastSeenUtc = DateTime.UtcNow;
                        pairedByMac.Add(mac, paired);
                        justConnectedTeachers.Add(teacher.TeacherName);
                    }
                    else
                    {
                        PairedTeacherDevice existing = pairedByMac[mac];
                        if (!existing.IsConnected)
                        {
                            existing.IsConnected = true;
                            justConnectedTeachers.Add(teacher.TeacherName);
                        }
                        existing.DeviceName = scanned.DeviceName;
                        existing.LastSeenUtc = DateTime.UtcNow;
                    }
                }
                else
                {
                    if (pairedByMac.ContainsKey(mac))
                    {
                        PairedTeacherDevice existing = pairedByMac[mac];
                        if (existing.IsConnected)
                        {
                            existing.IsConnected = false;
                            justDisconnectedTeachers.Add(existing.Teacher.TeacherName);
                        }
                    }
                }
            }
        }

        int connectedIndex = 0;
        while (connectedIndex < justConnectedTeachers.Count)
        {
            string teacherName = justConnectedTeachers[connectedIndex];
            connectedIndex++;
            RaiseStatusMessage(teacherName + " connected");
        }

        int disconnectedIndex = 0;
        while (disconnectedIndex < justDisconnectedTeachers.Count)
        {
            string teacherName = justDisconnectedTeachers[disconnectedIndex];
            disconnectedIndex++;
            RaiseStatusMessage(teacherName + " disconnected");
        }

        RaisePairingStateChanged();
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

    public List<PairedTeacherDevice> GetSnapshot()
    {
        List<PairedTeacherDevice> snapshot = new List<PairedTeacherDevice>();
        lock (syncRoot)
        {
            foreach (KeyValuePair<string, TeacherRecord> teacherEntry in teachersByMac)
            {
                string mac = teacherEntry.Key;
                TeacherRecord teacher = teacherEntry.Value;
                if (pairedByMac.ContainsKey(mac))
                {
                    PairedTeacherDevice source = pairedByMac[mac];
                    PairedTeacherDevice clone = new PairedTeacherDevice();
                    clone.Teacher = source.Teacher;
                    clone.DeviceName = source.DeviceName;
                    clone.IsConnected = source.IsConnected;
                    clone.LastSeenUtc = source.LastSeenUtc;
                    snapshot.Add(clone);
                }
                else
                {
                    PairedTeacherDevice notSeenYet = new PairedTeacherDevice();
                    notSeenYet.Teacher = teacher;
                    notSeenYet.DeviceName = "Not detected yet";
                    notSeenYet.IsConnected = false;
                    notSeenYet.LastSeenUtc = DateTime.MinValue;
                    snapshot.Add(notSeenYet);
                }
            }
        }
        return snapshot;
    }

    public bool HasAnyConnectedTeacher()
    {
        lock (syncRoot)
        {
            foreach (KeyValuePair<string, PairedTeacherDevice> pair in pairedByMac)
            {
                if (pair.Value.IsConnected)
                {
                    return true;
                }
            }
        }
        return false;
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
        Action<List<PairedTeacherDevice>> handler = PairingStateChanged;
        if (handler == null)
        {
            return;
        }

        List<PairedTeacherDevice> snapshot = GetSnapshot();
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
        public string MacAddress;
    }
}
