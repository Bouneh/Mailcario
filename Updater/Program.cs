using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace MailcarioUpdater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < args.Length; i += 2) values[args[i]] = args[i + 1];
            int parentPid = int.Parse(values["--wait-pid"]);
            string installer = Path.GetFullPath(values["--installer"]);
            string app = Path.GetFullPath(values["--app"]);
            string installDirectory = Path.GetDirectoryName(app)!;
            string expectedHash = values["--sha256"];
            if (!File.Exists(installer) || !File.Exists(app) || expectedHash.Length != 64)
                throw new InvalidDataException("The update files are missing or invalid.");

            try
            {
                using Process parent = Process.GetProcessById(parentPid);
                if (!parent.WaitForExit(120_000)) throw new TimeoutException("Mailcario did not close in time.");
            }
            catch (ArgumentException) { /* The parent already closed. */ }

            using (FileStream file = File.OpenRead(installer))
            {
                string actualHash = Convert.ToHexString(SHA256.HashData(file));
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update checksum does not match the release.");
            }

            using Process setup = Process.Start(new ProcessStartInfo(installer)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(installer)!,
                ArgumentList = { "/SP-", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                    "/CLOSEAPPLICATIONS", "/DIR=" + installDirectory }
            }) ?? throw new IOException("Unable to start the installer.");
            setup.WaitForExit();
            if (setup.ExitCode != 0) throw new IOException($"The installer returned code {setup.ExitCode}.");
            if (!File.Exists(app)) throw new FileNotFoundException("Mailcario was not found after installation.", app);
            Process.Start(new ProcessStartInfo(app)
            {
                UseShellExecute = true,
                WorkingDirectory = installDirectory
            });
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Mailcario update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
