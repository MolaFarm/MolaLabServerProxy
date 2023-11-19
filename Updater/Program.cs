using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Updater;

internal static class Program
{
    private static string _baseAddress;
    private static string _realHost;

    private static async Task Main(string[] args)
    {
        var token = args[0];
        var projectId = args[1];
        var serverIp = args[2];
        var url = new Uri(args[3]);
        var location = args[4];
        var updatePackagePath = location + "/update.zip";

        var jobId = GetJobIdFromUri(url.ToString());
        _baseAddress = $"https://{serverIp}/";
        _realHost = url.Host;

        Console.WriteLine("正在下载更新...");
        await Download(projectId, jobId, token, updatePackagePath);
        Console.WriteLine("正在安装更新...");
        KillMainProcess(location);
        Install(updatePackagePath, location);
        Console.WriteLine("正在重启程序...");
        Process.Start(new ProcessStartInfo
        {
            FileName = $"{location}\\ServerProxy.exe",
            UseShellExecute = true,
            CreateNoWindow = true,
            WorkingDirectory = location
        });
    }

    private static int GetJobIdFromUri(string uri)
    {
        var regex = new Regex(@"/jobs/(\d+)/");

        var match = regex.Match(uri);

        if (match.Success)
        {
            var jobIdString = match.Groups[1].Value;

            if (int.TryParse(jobIdString, out var jobId)) return jobId;
        }

        throw new ArgumentException("Failed to Get JOBID");
    }

    private static HttpClient CreateHttpClient(string token)
    {
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseAddress)
        };

        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
        client.DefaultRequestHeaders.Host = _realHost;

        return client;
    }

    private static async Task Download(string projectId, int jobId, string token, string savePath)
    {
        using var client = CreateHttpClient(token);
        var response = await client.GetAsync($"api/v4/projects/{projectId}/jobs/{jobId}/artifacts");
        response.EnsureSuccessStatusCode();

        using (Stream contentStream = await response.Content.ReadAsStreamAsync(),
               stream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await contentStream.CopyToAsync(stream);
            stream.Close();
        }
    }

    private static void Install(string updatePackagePath, string targetPath)
    {
        var tempDir = targetPath + "\\tmp";
        if (Path.Exists(tempDir)) Directory.Delete(tempDir, true);

        Directory.CreateDirectory(tempDir);
        ZipFile.ExtractToDirectory(updatePackagePath, tempDir);

        var mainProgram = new DirectoryInfo(tempDir).GetFiles("ServerProxy.exe", SearchOption.AllDirectories)[0];
        var newFilesRoot = mainProgram.Directory;
        try
        {
            newFilesRoot.GetFiles("Updater.exe")[0].MoveTo(newFilesRoot + "\\Updater.exe.new");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"警告：更新包似乎没有包含新的 Updater => {ex}");
        }


        var removeDictList = new DirectoryInfo(targetPath).GetDirectories().Where(dict => dict.Name != "tmp").ToList();
        var removeFileList = new DirectoryInfo(targetPath).GetFiles()
            .Where(file => !file.Name.StartsWith("Updater.") && file.Name != "config.json").ToList();
        foreach (var removeFile in removeFileList) removeFile.Delete();

        foreach (var removeDict in removeDictList) removeDict.Delete(true);

        foreach (var newFile in newFilesRoot.GetFiles().ToList()) newFile.MoveTo($"{targetPath}\\{newFile.Name}");

        Directory.Delete(tempDir, true);
    }

    private static void KillMainProcess(string location)
    {
        var processes = Process.GetProcessesByName("ServerProxy");
        foreach (var process in processes)
            if (process.MainModule?.FileName == location + "\\ServerProxy.exe")
            {
                Console.WriteLine("检测到程序正在运行，正在杀死进程...");
                process.Kill();
            }
    }
}