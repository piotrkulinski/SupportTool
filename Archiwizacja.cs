using ArchivesTools;
using System;
using System.IO;
using Ionic.Zip;
using System.Threading.Tasks;
using System.Text;
using System.Data;
using System.Collections.Generic;
using System.Management;
using static ArchivesTools.ArchiveItem;
using System.Data.Odbc;
using System.Runtime.InteropServices;
using System.Collections;

namespace ArchivesTools {
    public class Archiwizacja {
        private string progress_message = "";
        private string SpecialPassword = "";
        private STATE state = STATE.UNKNOW;
        private RESULT result = RESULT.UNKNOW;

        public event EventHandler<OnProgressBackup> OnProgressEvent;

        private Configuration _configuration = null;
        public Configuration configuration {
            get => _configuration;
        }
        private PrepareConfiguration prep = null;
        private FileInfo configurationGastro = null;

        private static readonly object _progress_message_locker_ = new object();
        private static readonly object _state_locker_ = new object();
        private static readonly object _result_locker_ = new object();

        private ArchiveItem backupItem;

        public STATE State {
            get {
                lock (_state_locker_) {
                    return state;
                }
            }
            set {
                lock (_state_locker_) {
                    state = value;
                }
            }
        }
        public RESULT Result {
            get {
                lock (_result_locker_) {
                    return result;
                }
            }
            set {
                lock (_result_locker_) {
                    result = value;
                }
            }
        }
        private string ProgressMessage {
            get {
                lock (_progress_message_locker_) {
                    return progress_message;
                }
            }
            set {
                lock (_progress_message_locker_) {
                    progress_message = value;
                }
            }
        }

        public Archiwizacja() {
            Console.WriteLine("init");
        }
        ~Archiwizacja() {

        }

        public string GetProgress() {
            string message = ProgressMessage;
            if (message.Length > 0) {
                ProgressMessage = "";
            }
            return message;
        }
        public void SetProgress(string cMessage) {
            OnProgressEvent?.Invoke(this, new OnProgressBackup(cMessage));
            ProgressMessage = cMessage;
        }
        public string GetSessionKey() {
            return "";
        }
        public void SetPassword(string passwordBase64) {
            if (!String.IsNullOrWhiteSpace(passwordBase64)) {
                byte[] passwordBytes = Convert.FromBase64String(passwordBase64);
                SpecialPassword = Encoding.Default.GetString(passwordBytes);
            }
        }

        private class DateComparer : IComparer {
            public int Compare(object x1, object y1) {
                DirectoryInfo x = (DirectoryInfo)x1;
                DirectoryInfo y = (DirectoryInfo)y1;
                return x.CreationTime > y.CreationTime ? -1 : x.CreationTime < y.CreationTime ? 1 : 0;
            }
        }

        public int LeaveOnlyLastArchives(int lastCount) {
            if (_configuration is null) {
                string message = "Nie ustawiono konfiguracji, czyszczenie archiwum niedozwolone";
                ProgressMessage = message;
                OnProgressEvent?.Invoke(this, new OnProgressBackup(message));
                throw new Exception(message);
            }
            DirectoryInfo di = new DirectoryInfo(_configuration.Settings.DirectoryZip);

            if (di.FullName == di.Root.FullName) {
                SetProgress($"Katalog główny dysku, pomijam czyszczenie katalogów");
                return -1;
            }
            DirectoryInfo[] test = di.Parent.GetDirectories();
            Array.Sort(test, new DateComparer());
            int counter = 0;
            foreach (DirectoryInfo d in test) {
                if (counter++ >= lastCount) {
                    try {
                        SetProgress($"Usuwanie przedawnionego archiwum (opcja nieaktywna)\r{d.FullName}");
                    }
                    catch (Exception ex) {
                        SetProgress(ex.Message);
                    }
                }
            }
            return 1;
        }

        public void SetConfiguration(string configurationXml) {
            prep = new PrepareConfiguration();
            if (configurationGastro != null) {
                prep.SetConfigurationGastro(configurationGastro);
            }
            _configuration = prep.Prepare(configurationXml);
            if (backupItem != null) {
                _configuration.Items.Add(backupItem);
            }
        }
        public void SetConfiguration(FileInfo ConfigurationFile) {
            prep = new PrepareConfiguration(ConfigurationFile.FullName);
            if (configurationGastro != null) {
                prep.SetConfigurationGastro(configurationGastro);
            }
            _configuration = prep.Prepare(ConfigurationFile);
            if (backupItem != null) {
                _configuration.Items.Add(backupItem);
            }
        }
        public void SetConfigurationGastro(FileInfo ConfigurationFile) {
            configurationGastro = ConfigurationFile;
        }
        public string GetPath(string xpath) {
            string value = prep?.GetPath(xpath);
            if (value == null) {
                return "";
            }
            return value;
        }

        public void GetInfoSystemLog() {
            if (prep != null) {
                prep.GetInfoSystemLog();
            }
        }

        /// <summary>
        /// Piotr Kuliński (c) 2020
        /// Zażądanie wykonania backupu bazy danych SQL<br>
        /// Uwaga, wymagane jest wcześniejsze ustawienie konfiguracji</br>
        /// </summary>
        /// <param name="odbcDNS"></param>
        /// <param name="odbcUID"></param>
        /// <param name="odbcPWD"></param>
        public void SetBackupDatabase(ArchiveDB archiveDb) {
            backupItem = new ArchiveItem();
            backupItem.backupDb = archiveDb;
            backupItem.zipfile = $"{archiveDb.GetDbName()}.zip";
            backupItem.path = "c:\\SqlBackup"; //utworzenie udostępnionej ścieżki
            backupItem.recurse = "0";
            backupItem.SendToSynology = "1";
            backupItem.patterns = new List<Pattern>();
            Pattern p = new Pattern();
            //usuwamy, bo to za wielkie pliki
            p.deleteAfterAdd = true;

            //zbierze najświeższe
            //DateTime dt = DateTime.Now - TimeSpan.FromMinutes(10);
            //p.pattern = $"name = {odbcDNS}.bak and mtime>{dt.ToString("yyyy-MM-dd-HH:mm:ss")}";
            p.pattern = $"{archiveDb.GetDbName()}.*";
            backupItem.patterns.Add(p);
            if (_configuration != null) {
                _configuration.Items.Add(backupItem);
                //todo: tylko test sprawdzający, usunąć
                //string buff = Helpers.Serialize<Configuration>(configuration);
                //Console.WriteLine(buff);
            }
        }

        public void AsyncCreateArchive() {
            Task taskA = new Task(() => { CreateArchive(); });
            taskA.Start();
            State = STATE.ZIP_START;
        }

        public void CreateArchive() {
            Result = RESULT.UNKNOW;
            State = STATE.ZIP_START;
            try {
                PackageData pkg = new PackageData(_configuration);
                if (SpecialPassword.Length == 0) {
                    var generator = new PasswordGenerator();
                    SpecialPassword = generator.Generate();
                }
                pkg.AddProgressEvent += ZippProgresToFox;
                pkg.MyProgressEvent += MyProgress;
                pkg.Compress(SpecialPassword);

                if (prep != null) {
                    _configuration.Scripts.ForEach(script => {
                        if (script != null && script.Action.Equals(ActionType.After)) {
                            PrepareConfiguration.RunScript(script);
                        }
                    });
                }
                Result = RESULT.OK;
            }
            catch (Exception ex) {
                Result = RESULT.FAILED;
                Console.WriteLine(ex.Message);
            }
            State = STATE.ZIP_STOP;
        }

        private void MyProgress(object sender, OnProgressBackup e) {
            ProgressMessage = String.Format("{0}", e.ToString());
        }
        private void ZippProgresToFox(object sender, AddProgressEventArgs e) {
            if (e.CurrentEntry != null && e.CurrentEntry.FileName != null) {
                ProgressMessage = String.Format("{0}\r{1}", e.ArchiveName, e.CurrentEntry.FileName);
            }
        }
        private void ShowProgressUpload(String message, params object[] args) {
            ProgressMessage = String.Format("{0}\r{1}", message, args[0]);
        }

        public void AsyncSendToServer() {
            Task taskA = new Task(() => { SendToServer(); });
            taskA.Start();
            State = STATE.SEND_FTP_START;
        }

        public void SendToServer() {
            State = STATE.SEND_FTP_START;
            Result = RESULT.UNKNOW;
            try {
                SendData ftp = new SendData(_configuration);
                ftp.OnShowProgress += ShowProgressUpload;
                if (ftp.Upload() == 1) {
                    Result = RESULT.OK;
                }
                else {
                    Result = RESULT.FAILED;
                }
            }
            catch (Exception ex) {
                Result = RESULT.FAILED;
            }
            State = STATE.SEND_FTP_STOP;
        }
        public void DownloadPackageFromSynology(string sourceFilename, string destinationFilename) {
            State = STATE.WORKING;
            Result = RESULT.UNKNOW;
            ProgressMessage = "";
            try {
                DownloadFromSynology syn = new DownloadFromSynology();
                syn.OnShowProgress += ShowProgressUpload;
                syn.synology.OnShowProgress += ShowProgressUpload;
                if (syn.DownloadFile(sourceFilename, destinationFilename)) {
                    Console.WriteLine("RESULT.OK");
                    Result = RESULT.OK;
                }
                else {
                    Console.WriteLine("RESULT.FAILED");
                    Result = RESULT.FAILED;
                }
            }
            catch (Exception ex) {
                Result = RESULT.FAILED;
            }
            State = STATE.FINISH;
            Console.WriteLine("STATE.FINISH");
        }
        public void AsyncDownloadPackageFromSynology(string sourceFilename, string destinationFilename) {
            #region Zanim wątek wystaruje ustawmy, aby można było sprawdzać w pętli prog. implement.
            State = STATE.WORKING;
            Result = RESULT.UNKNOW;
            #endregion
            Task taskA = new Task(() => { DownloadPackageFromSynology(sourceFilename, destinationFilename); });
            taskA.Start();
        }

        public void Decompress() {
            PackageData unpack = new PackageData(configuration);
            unpack.Decompress();
        }

        private void Zip_ExtractProgress(object sender, ExtractProgressEventArgs e) {
            if (e.CurrentEntry != null && e.CurrentEntry.FileName != null) {
                double prc = (e.BytesTransferred * 1d) / (Math.Max(1,e.TotalBytesToTransfer) * 1d) * 100;                
                ProgressMessage = String.Format(System.Globalization.CultureInfo.GetCultureInfo("en-GB"), "{0}\rdekompresja: {1,6:0.00} %", e.CurrentEntry.FileName,prc);
               // Console.WriteLine("Extract {0} <- {1}", e.ArchiveName, e.CurrentEntry.FileName);
            }
        }

        public void ExtractZip(string zipFile, string selectionCriteria = "name = *.*", string directoryPathInArchive = null, string extractDirectory = ".\\") {
            State = STATE.WORKING;
            Result = RESULT.UNKNOW;
            ProgressMessage = "";
            try {
                ZipFile zip = new ZipFile(zipFile) {
                    //Password = _password,
                    //Encryption = EncryptionAlgorithm.WinZipAes256
                };
                zip.ExtractProgress += Zip_ExtractProgress;
                zip.ExtractSelectedEntries(selectionCriteria, directoryPathInArchive, extractDirectory, ExtractExistingFileAction.OverwriteSilently);
                Result = RESULT.OK;
            }
            catch (Exception ex) {
                Result = RESULT.FAILED;
            }
            State = STATE.FINISH;
        }

        public void AsyncExtractZip(string zipFile, string selectionCriteria = "name = *.*", string directoryPathInArchive = null, string extractDirectory = ".\\") {
            #region Zanim wątek wystaruje ustawmy, aby można było sprawdzać w pętli prog. implement.
            State = STATE.WORKING;
            Result = RESULT.UNKNOW;
            #endregion
            Task taskA = new Task(() => { ExtractZip(zipFile, selectionCriteria, directoryPathInArchive, extractDirectory); });
            taskA.Start();
        }
    }
}
