using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;
using System.Xml;

namespace ArchivesTools {
    public class PrepareConfiguration {

        private XmlDocument odom = null;
        private FoxWrapper fox = null;
        private FileInfo confClient = new FileInfo("client.xml");
        public PrepareConfiguration() {
            fox = new FoxWrapper();
        }
        public PrepareConfiguration(string ConfigurationFile) : this() {
            if (!File.Exists(ConfigurationFile)) {
                ConfigurationFile = "configuration\\" + ConfigurationFile;
            }

            if (!File.Exists(ConfigurationFile)) {
                throw new ApplicationException($"Brak pliku konfiguracyjnego: {ConfigurationFile}");
            }

        }

        public void SetConfigurationGastro(FileInfo _conf) {
            confClient = _conf;
            fox = new FoxWrapper(confClient.FullName);
        }

        public Configuration Prepare(FileInfo FileConfiguration) {
            string xmlConfiguration = File.ReadAllText(FileConfiguration.FullName, Encoding.Default);
            return Prepare(xmlConfiguration);
        }

        public Configuration Prepare(string xmlConfiguration) {
            if (fox == null) {
                fox = new FoxWrapper();
            }
            odom = new XmlDocument();
            odom.LoadXml(xmlConfiguration); //.Load(ConfigurationFile);

            Configuration arc = Helpers.Deserialize<Configuration>(xmlConfiguration);

            int nCountFoxMacro = arc.Macros.Count;
            if (nCountFoxMacro == 0) {
                arc.Macros.Add(new ArchiveMacro() { Name = "%Date%", Value = "yyyyMMdd", Type = MacroType.Datetime });
                arc.Macros.Add(new ArchiveMacro() { Name = "%Time%", Value = "HHmmss", Type = MacroType.Datetime });

                string _macro = "%UniqueClientNumber%";
                if (odom.OuterXml.Contains(_macro)) {
                    nCountFoxMacro++;
                    arc.Settings.UniqueClientNumber = arc.Macros.Find((e) => e.Name == _macro).Value;
                }

                _macro = "%DataBase%";
                if (odom.OuterXml.Contains(_macro)) {
                    nCountFoxMacro++;
                    arc.Macros.Add(new ArchiveMacro() { Name = _macro, Value = "BAZA", Type = MacroType.Fox });
                }

                _macro = "%TransactionBase%";
                if (odom.OuterXml.Contains(_macro)) {
                    nCountFoxMacro++;
                    arc.Macros.Add(new ArchiveMacro() { Name = _macro, Value = "BAZATRANS", Type = MacroType.Fox });
                }

                _macro = "%SessionID%";
                if (odom.OuterXml.Contains(_macro)) {
                    nCountFoxMacro++;
                    arc.Settings.SessionID = arc.Macros.Find((e) => e.Name == _macro).Value;
                }
            }

            foreach (ArchiveMacro macro in arc.Macros) {
                if (macro.Type == MacroType.Fox) {
                    string valueVar = fox.GetVariable(macro.Value);
                    Console.WriteLine($"Zmienna fox: {macro.Value}, wartość: {valueVar}, makro: {macro.Name}");
                    macro.Value = valueVar;
                }
                else if (macro.Type == MacroType.Datetime) {
                    macro.Value = String.Format("{0:" + macro.Value + "}", DateTime.Now);
                }
                else if (macro.Type == MacroType.Environment) {
                    string parseValue = Environment.GetEnvironmentVariable(macro.Value);
                    if (!(parseValue is null)) {
                        macro.Value = parseValue;
                    }
                    else if (macro.Value.Contains("CD")) { //własne dodatkowe makro
                        parseValue = Directory.GetCurrentDirectory();
                        int last = parseValue.LastIndexOf('\\');
                        if (last > 3) {
                            macro.Value = parseValue.Substring(last + 1);
                        }
                    }
                }
                else {
                    //
                }

                foreach (ArchiveItem item in arc.Items) {
                    item.path = item.path.Replace(macro.Name, macro.Value);
                    item.zipfile = item.zipfile.Replace(macro.Name, macro.Value);
                }

                if (macro.Name.Contains("%SessionID%")) {
                    arc.Settings.SessionID = arc.Settings.SessionID.Replace(macro.Name, macro.Value);
                }
                if (macro.Name.Contains("%UniqueClientNumber%")) {
                    arc.Settings.UniqueClientNumber = arc.Settings.UniqueClientNumber.Replace(macro.Name, macro.Value);
                    arc.Settings.UniqueClientNumber = arc.Settings.UniqueClientNumber.Replace(" ", "").Replace("-", "");
                }
                arc.Settings.DirectoryZip = arc.Settings.DirectoryZip.Replace(macro.Name, macro.Value);
                arc.Settings.DirectoryServer = arc.Settings.DirectoryServer.Replace(macro.Name, macro.Value);
                arc.Settings.Password = arc.Settings.Password.Replace(macro.Name, macro.Value);

                foreach (ArchiveScript item in arc.Scripts) {
                    item.Arguments = item.Arguments.Replace(macro.Name, macro.Value);
                    item.Command = item.Command.Replace(macro.Name, macro.Value);
                    item.FileOutput = item.FileOutput.Replace(macro.Name, macro.Value);
                }
            }

            arc.Scripts.ForEach(script => {
                if (script != null && script.Action.Equals(ActionType.Before)) {
                    RunScript(script);
                }
            });


            odom.LoadXml(Helpers.Serialize<Configuration>(arc));

            return arc;
        }

        public static void RunScript(ArchiveScript script) {
            string filename = script.Command;
            string fileout = script.FileOutput;
            try {
                using (Process compiler = new Process()) {
                    compiler.StartInfo.FileName = filename;
                    compiler.StartInfo.Arguments = script.Arguments;

                    compiler.StartInfo.RedirectStandardOutput = true; // (redirect >= 0);
                    compiler.StartInfo.UseShellExecute = false; // !compiler.StartInfo.RedirectStandardOutput;
                    compiler.StartInfo.CreateNoWindow = true;
                    if (compiler.StartInfo.RedirectStandardOutput) {
                        compiler.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                    }
                    else {
                        compiler.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    }
                    compiler.Start();
                    if (compiler.StartInfo.RedirectStandardOutput && !String.IsNullOrEmpty(fileout)) {
                        string buffer = compiler.StandardOutput.ReadToEnd();
                        string cDir = Directory.GetParent(fileout).FullName;
                        if (!Directory.Exists(cDir)) {
                            Directory.CreateDirectory(cDir);
                        }
                        File.WriteAllText(fileout, buffer);
                    }
                    compiler.WaitForExit();
                }
            }
            catch (Exception ex) {
                Console.Error.WriteLine(ex.ToString());
            }
        }

        public string GetPath(string xpath) {
            if (odom != null) {
                return odom.SelectSingleNode(xpath).InnerText;
            }
            return "";
        }

        public void GetInfoSystemLog() {
            using (StreamWriter sw = new StreamWriter("System.log", false)) {
                if ((bool)fox?.isLoadConfigPOS) {
                    sw.WriteLine("---- Klient ----");
                    sw.WriteLine(String.Format("Klient: {0}", fox.GetVariable("NAZWA_KLIENTA")));
                    sw.WriteLine(String.Format("Miasto: {0}", fox.GetVariable("MIASTO")));
                    sw.WriteLine(String.Format("Ulica: {0}", fox.GetVariable("ULICA")));
                    sw.WriteLine("---- Klient ----");
                }

                String[] sub = {
                        "Win32_ComputerSystem"
                        , "Win32_LogicalDisk"
                        , "Win32_OperatingSystem"
                };
                foreach (String subsystem in sub) {
                    sw.WriteLine("-".PadLeft(100, '-'));
                    sw.WriteLine($"{subsystem}");
                    sw.WriteLine("-".PadLeft(100, '-'));
                    ManagementObjectSearcher search = new ManagementObjectSearcher($"SELECT * FROM {subsystem}");
                    foreach (ManagementObject obj in search.Get()) {
                        PropertyDataCollection props = obj.Properties;
                        foreach (PropertyData prop in props) {
                            if (prop.Value != null)
                                sw.WriteLine($"\t{prop.Name} -> {prop.Value}");
                        }
                    }
                }
                sw.Flush();
                sw.Close();
            }

        }
    }
}

