using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Diagnostics;

namespace Apollo.Functions
{
    public static class EncodeVideo
    {
        private static readonly string FFMpegPath = Environment.GetEnvironmentVariable("FFMPEG:Path");
        private static readonly string FFProbePath = Environment.GetEnvironmentVariable("FFPROBE:Path");
        private const string VideoEncodedNameExtension = ".h264";

        [FunctionName(nameof(Encode))]
        public static async Task Encode(
            [BlobTrigger("network/{name}.{ext}")] Stream inputBlob,
            string name,
            string ext,
            ExecutionContext context,
            [Blob("network/{name}.{ext}" + VideoEncodedNameExtension + ".mp4", FileAccess.ReadWrite)] CloudBlockBlob output,
            TraceWriter log)
        {

            if (!VideoEncodedNameExtension.Contains("." + ext.ToLower()) || name.EndsWith(VideoEncodedNameExtension))
                return;

            byte[] bytes;

            using (var ms = new MemoryStream())
            {
                inputBlob.CopyTo(ms);
                bytes = ms.ToArray();
            }

            var tempPath = Path.Combine(context.FunctionDirectory, "temp" + Guid.NewGuid().ToString("N"));
            var tempResultPath = tempPath + VideoEncodedNameExtension;

            try
            {
                File.WriteAllBytes(tempPath, bytes);

                var exe = Path.Combine(context.FunctionDirectory, FFMpegPath);
                var crop = ", crop = 512:512";
                if (name.EndsWith(".l"))
                    crop = string.Empty;

                var cmdScaleCrop = $" -i {tempPath} -f mp4 -c:v libx264 -r 30 -vf \"scale = (iw * sar) * max(512 / (iw * sar)\\, 512 / ih):ih* max(512 / (iw * sar)\\,512 / ih){crop}\" -c:a aac -strict -2 -b:a 128k -ar 44100 {tempResultPath}";
                var process = new Process
                {
                    StartInfo =
                {
                    FileName = exe,
                    Arguments = cmdScaleCrop,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true
                }
                };

                process.Start();
                process.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds);

                //save the video to blob
                var data = File.ReadAllBytes(tempResultPath);
                output.Properties.ContentType = "video/mp4";
                await output.UploadFromByteArrayAsync(data, 0, data.Length);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                if (File.Exists(tempResultPath))
                    File.Delete(tempResultPath);
            }
        }
    }
}
