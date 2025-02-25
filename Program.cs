using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using ArchivesTools;
using System.Windows.Navigation;
using System.Data.Odbc;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.Remoting.Messaging;

namespace Archiwizuj {
    class Program {
        static void Main(string[] args) {
            List<String> parameters = new List<string>();
            string ConfigurationFile = "archiwizacja.xml";
            string SpecialPassword = "";
            int nleave = -1;
            bool needPassword = false;
            int encryptionCertificateCount = Directory.GetFiles(".\\", "*.cer").Length;

            try {
                for (int I = 0; I < args.Length; I++) {
                    parameters.Add(args[I].ToLower());
                }
                needPassword = !parameters.Contains("--password");

                if (parameters.Count == 0 || parameters.Any((el) => el.Contains("?") || el.Contains("/h") || el.Contains("help"))) {
                    Console.Clear();
                    Console.WriteLine();
                    Console.WriteLine($"{AppDomain.CurrentDomain.FriendlyName} [--srv] [--unpack [--dd <dir> [dest]]] [--configuration <FileConfiguration.xml>] [--password <password>]");
                    Console.WriteLine("usage:");
                    Console.WriteLine("\t--srv - kompresowanie, szyfrowanie i wysyłka na serwer producenta");
                    Console.WriteLine("\t--unpack - rozszyfrowanie danych, może zrobić to jedynie wystawca certyfikatu, którego użyto do zaszyfrowania");
                    //Console.WriteLine("\t--background - aplikacja jest uruchamiana w tle");
                    //Console.WriteLine("\t--info - dodatkowe zebranie informacji o systemie");
                    Console.WriteLine("\t--configuration <FileConfiguration.xml> - podanie innej konfiguracji niż domyślna (archiwizacja.xml)");
                    Console.WriteLine("\t--dd <dir> <dest> - katalog w którym są szukane zarchiwizowane dane, wypakować do <dest>");
                    Console.WriteLine("\t--password <password> - własne hasło dla spakowanych danych (niezalecane, jedynie na polecenie serwisu)");
                    Console.WriteLine("\t--backupdb <\"connection_string\"> - kopia bazy, no. \"DSN=sqlodbc;UID=sa;PWD=haslo_uzytkownik_backup\"");
                    Console.WriteLine("\t--leave [<ncount>] - czyszczenie archiwum, pozostawienie ncount (default:3) ostatnich kopii");
                    Console.WriteLine();
                    Console.WriteLine("Oprogramowanie pozwala przygotować dane przeznaczone do analizy serwisowej");
                    Console.WriteLine("Dane zostaną zaszyfrowane pod warunkiem dostarczenia przez odbiorcę certyfikatu");
                    Console.WriteLine("upoważniającego do wysyłki. Jedynie odbiorca dostarczonego certyfikatu będzie mógł te dane pozyskać.");
                    Console.WriteLine("Tylko paczki zabezpieczone hasłem mogą zostać wysłane na serwer (parametr --srv)");
                    Console.WriteLine();
                    if (encryptionCertificateCount == 0) {
                        ConsoleColor lastColor = Console.ForegroundColor;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("W katalogu brak certyfikatów szyfrujących.");
                        if (needPassword) {
                            Console.WriteLine("Do zaszyfrowania będzie wymagane podanie hasła.");
                        }
                        Console.ForegroundColor = lastColor;
                        Console.WriteLine();
                    } else {
                        Console.WriteLine("Katalog zawiera certyfikaty szyfrujące.");
                        Console.WriteLine();
                        needPassword = false;
                    }
                    Console.WriteLine("Wybierz opcję, klawisz spoza listy zamyka okno bez działania...");
                    Console.WriteLine();
                    Console.WriteLine("\t[K] - kompresja bez wysyłki");
                    Console.WriteLine("\t[F] - kompresja z wysyłką na FTP");
                    Console.WriteLine("\t[D] - odzyskaj dane skompresowane (wymaga certyfikatu klucza prywatnego)");
                    Console.WriteLine();
                    Console.Write("Wybór: ");

                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.KeyChar == 'S' || k.KeyChar == 's' || k.KeyChar == 'F' || k.KeyChar == 'f') {
                        if (needPassword) {
                            if (!InputPassword(parameters)) {
                                return;
                            }
                        }
                        Console.WriteLine("Kompresja z wysyłką");
                        parameters.Add("--srv");
                        parameters.Add("--info");
                    }
                    else if (k.KeyChar == 'D' || k.KeyChar == 'd') {
                        Console.WriteLine("Odzyskiwanie danych");
                        parameters.Add("--unpack");
                    }
                    else if (k.KeyChar == 'K' || k.KeyChar == 'k' ) {                        
                        if (needPassword) {
                            if (!InputPassword(parameters)) {
                                return;
                            }
                        }
                        Console.WriteLine("Kompresja bez wysyłki");
                        parameters.Add("--info");
                    }
                    else
                        return;
                    Console.WriteLine();
                }
                
                for (int I = 0; I < parameters.Count; I++) {
                    string arg = parameters[I].ToLower();

                    if (arg.Contains("--configuration") && parameters.Count >= (I + 1)) {
                        if (!File.Exists(parameters[++I])) {
                            Console.WriteLine($"Brak wskazanego pliku konfiguracyjnego '{parameters[I]}'");
                            return;
                        }
                        else {
                            ConfigurationFile = parameters[I];
                            // break;
                        }
                    }
                    else if (arg.Contains("--password") && parameters.Count >= (I + 1)) {
                        string xSpecialPassword = parameters[++I];
                        if (xSpecialPassword.Length < 5) {
                            Console.WriteLine($"Hasło musi mieć przynajmniej 5 znaków (bez spacji)");
                            return;
                        }
                        SpecialPassword = Convert.ToBase64String(Encoding.Default.GetBytes(xSpecialPassword));
                    }
                }

                string xmlConfiguration = File.ReadAllText(ConfigurationFile, Encoding.Default);

                Archiwizacja arch = new Archiwizacja();
                arch.OnProgressEvent += ShowProgress;
                arch.SetConfiguration(xmlConfiguration);

                for (int I = 0; I < args.Length; I++) {
                    string arg = args[I].ToLower();
                    int _inc = 0;
                    if (arg == "--dd") {
                        if ((I + 1) < args.Length && args[I + 1].Substring(0, 2) != "--") {
                            _inc++;
                            arch.configuration.ArchiveDirectory = args[I + 1];
                        }

                        if ((I + 2) < args.Length && args[I + 2].Substring(0, 2) != "--") {
                            _inc++;
                            arch.configuration.ExtractDirectory = args[I + 2];
                        }
                    }
                    else if (arg == "--leave") {
                        nleave = 3;
                        if ((I + 1) < args.Length && args[I + 1].Substring(0, 1) != "-") {
                            _inc++;
                            if (Int32.TryParse(args[I + 1].Trim(), out nleave)) {
                            }
                        }                       
                    }
                    else if (arg == "--backupdb") {
                        if ((I + 1) < args.Length && args[I + 1].Substring(0, 2) != "--") {
                            _inc++;
                            string connectionString = args[I + 1];
                            if (connectionString.ToLower().Contains("dsn=")) {
                                OdbcConnection connection = new OdbcConnection(connectionString);
                                connection.Open();
                                if (connection.State == ConnectionState.Open) {
                                    ArchiveDB odbc = new ArchiveODBC(connection);
                                    arch.SetBackupDatabase(odbc);
                                    connection.Close();
                                } else {
                                    Console.Error.WriteLine($"Błąd połączenia ODBC do {connectionString}");
                                }
                            }
                            else {
                                SqlConnection connection = new SqlConnection(connectionString);
                                connection.Open();
                                if (connection.State == ConnectionState.Open) {
                                    ArchiveDB sql = new ArchiveSQL(connection);
                                    arch.SetBackupDatabase(sql);
                                    connection.Close();
                                }
                                else {
                                    Console.Error.WriteLine($"Błąd połączenia SQL do {connectionString}");
                                }
                            }
                        }
                    }
                    I += _inc;
                }

                bool NoContinue = false;
                parameters.ForEach(
                (arg) => {
                    if (arg == "--info") {
                        arch.GetInfoSystemLog();
                    }

                    if (arg == "--unpack") {
                        arch.Decompress();
                        if (nleave > 0) {
                            arch.LeaveOnlyLastArchives(nleave);
                        }
                        NoContinue = true;
                    }
                }
                );

                if (NoContinue) {
                    return;
                }

                arch.SetPassword(SpecialPassword);
                arch.CreateArchive();
                if (parameters.Any(a => a.Equals("--srv")) && SpecialPassword.Length > 0) {
                    arch.SendToServer();
                }
                if (nleave > 0) {
                    arch.LeaveOnlyLastArchives(nleave);
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex.Message);
                Console.ReadKey();
            }
            finally {
                Console.WriteLine("-----");
                Console.WriteLine("end");
            }
        }

        private static bool InputPassword(List<string> param) {
            string pass = "";
            Console.WriteLine();
            Console.Write("Podaj hasło zabezpieczające (min: 5 znaków): ");
            ConsoleKeyInfo key;
            do {
                key = Console.ReadKey(true);
                if (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Escape) {
                    pass += key.KeyChar;
                    Console.Write('*');
                }
            } while (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Escape);
            if (key.Key == ConsoleKey.Escape) {
                return false;
            }
            if (String.IsNullOrEmpty(pass) || pass.Length < 5) {
                Console.WriteLine("Spróbuj ponownie, hasło nie spełnia wymogów");
                return false;
            }
            param.Add("--password");
            param.Add(pass);
            Console.WriteLine();
            return true;
        }

        public static void ShowProgress(object sender, OnProgressBackup e) {
            Console.WriteLine(e.ToString());
        }
    }
}
