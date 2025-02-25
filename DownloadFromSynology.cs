using ArchivesTools.Synology;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArchivesTools {
    public class DownloadFromSynology {
        public delegate void ShowProgressHandler(String message, params object[] args);
        public event ShowProgressHandler OnShowProgress;

        public SynologyClient synology = new SynologyClient();
        public bool DownloadFile(string sourceFile, string destinationFile) {
            try {
                synology.Username = Encoding.ASCII.GetString(Convert.FromBase64String("bfHNpX5fZG93bmxvYWQ="));
                synology.Password = Encoding.ASCII.GetString(Convert.FromBase64String("SD9aRNA=="));
                if (synology.Login("FileStation")) {
                    try {
                        //OnShowProgress?.Invoke("Download file", Path.GetFileName(destinationFile));
                        var result = synology.DownloadFileAsync(sourceFile, destinationFile);
                        while (result.Status != TaskStatus.RanToCompletion) {
                            Thread.Sleep(1000);
                        }
                        Console.WriteLine($"Result: {result.Result}");
                        return result.Result;
                    }
                    catch (Exception ex) {
                        Console.WriteLine(ex.ToString());
                        OnShowProgress?.Invoke("Download file filed", ex.ToString());
                    }
                    finally {
                        synology.Logout();
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }
            return false;
        }
    }
}
