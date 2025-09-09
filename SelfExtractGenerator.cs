using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Reflection;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;

namespace SelfExtractGenerator
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.Title = "Self-Extract Generator";

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== 自解压文件生成器 ===");
            Console.ResetColor();

            try
            {
                // 计算最大文件大小基于可用内存
                long maxFileSize = GetMaxFileSizeBasedOnMemory();
                Console.WriteLine("\n系统可用内存约: " + (maxFileSize / (1024 * 1024)) + " MB");
                Console.WriteLine("最大支持文件大小: " + (maxFileSize / (1024 * 1024)) + " MB ");
                Console.ResetColor();

                // 步骤1: 选择文件/文件夹（可多选）
                Console.WriteLine("\n步骤1: 选择文件/文件夹（可多选）");
                var selections = CollectSelections();
                if (selections == null || selections.Count == 0)
                {
                    Console.WriteLine("未选择任何内容，程序退出。");
                    Console.WriteLine("\n按任意键退出...");
                    Console.ReadKey();
                    return;
                }

                long totalSize = ComputeTotalSize(selections);
                if (totalSize > maxFileSize)
                {
                    throw new InvalidOperationException("选择内容过大（" + (totalSize / (1024 * 1024)) + " MB），超过系统可用内存限制 (" + (maxFileSize / (1024 * 1024)) + " MB)。请减少文件或分批处理。");
                }

                // 打包为 ZIP（统一打包，便于解压端展开）
                byte[] fileBytes = BuildZip(selections);
                string fileName = "Package_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip";
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("内容打包完成: " + fileName + " (" + fileBytes.Length + " 字节)");
                Console.ResetColor();

                // 步骤2: 指定输出文件夹
                Console.WriteLine("\n步骤2: 选择输出文件夹");
                string outputDir = SelectOutputFolder();
                if (string.IsNullOrEmpty(outputDir))
                {
                    Console.WriteLine("未选择输出文件夹，程序退出。");
                    Console.WriteLine("\n按任意键退出...");
                    Console.ReadKey();
                    return;
                }
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("输出文件夹创建成功: " + outputDir);
                    Console.ResetColor();
                }

                // 可选：用户自定义 key 与时间戳
                string userToken = PromptUserToken();
                long timestampTicks = DateTime.UtcNow.Ticks;

                // 步骤3: 生成自解压 EXE
                string exeName = "SelfExtractor_" + Path.GetFileNameWithoutExtension(fileName) + ".exe";
                string exePath = Path.Combine(outputDir, exeName);
                GenerateSelfExtractor(exePath, fileBytes, fileName, userToken, timestampTicks);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n自解压 EXE 生成成功: " + exePath);
                Console.WriteLine("运行该 EXE 时，它会提示指定释放路径，然后解密并解压ZIP内容。");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("错误: " + ex.Message);
                Console.ResetColor();
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        static string SelectSourceFile()
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Title = "选择要打包的文件",
                    Filter = "所有文件 (*.*)|*.*",
                    FilterIndex = 1,
                    RestoreDirectory = true
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    return openFileDialog.FileName;
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("文件选择错误: " + ex.Message);
                return null;
            }
        }

        static string SelectOutputFolder()
        {
            try
            {
                FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
                {
                    Description = "选择输出文件夹",
                    ShowNewFolderButton = true
                };

                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {
                    return folderBrowserDialog.SelectedPath;
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("文件夹选择错误: " + ex.Message);
                return null;
            }
        }

        static long GetMaxFileSizeBasedOnMemory()
        {
            try
            {
                PerformanceCounter availableMemoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                availableMemoryCounter.NextValue(); // 首次调用可能返回0
                float availableMB = availableMemoryCounter.NextValue();
                availableMemoryCounter.Dispose();
                return (long)(availableMB * 1024 * 1024 * 0.8); 
            }
            catch
            {
                // 如果PerformanceCounter失败，回退到默认值（例如1GB）
                return 1024L * 1024 * 1024;
            }
        }

        // 修改：可选输入 Key，取消长度限制
        static string PromptUserToken()
        {
            Console.Write("\n是否输入自定义 Key? (Y/n): ");
            string choice = Console.ReadLine();
            bool useKey = string.IsNullOrWhiteSpace(choice) || choice.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
            if (!useKey)
            {
                Console.WriteLine("将使用默认派生（不包含用户 Key）。");
                return string.Empty;
            }

            Console.Write("请输入 Key（可留空，直接回车使用默认）：");
            string key = Console.ReadLine();
            return key ?? string.Empty;
        }

        // AES对称加密
        // 替换：AES加密，随机IV，返回 IV||Ciphertext
        static byte[] EncryptData(byte[] data, byte[] key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                using (var ms = new MemoryStream())
                {
                    // 前16字节写入IV
                    ms.Write(aes.IV, 0, aes.IV.Length);
                    using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(data, 0, data.Length);
                    }
                    return ms.ToArray(); // IV + CIPHERTEXT
                }
            }
        }

        // 修改：派生AES-256密钥（兼容空Key）
        static byte[] DeriveKey(string userToken, string fileName, int fileSize, long timestampTicks)
        {
            string safeToken = userToken ?? string.Empty;
            string material = safeToken + "|" + fileName + "|" + fileSize.ToString() + "|" + timestampTicks.ToString() + "|SelfExtractor2024";
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(material)); // 32字节
            }
        }

        // 交互式收集多选路径（文件可多选，文件夹可多次添加）
        static System.Collections.Generic.List<string> CollectSelections()
        {
            var list = new System.Collections.Generic.List<string>();
            while (true)
            {
                Console.Write("添加 文件(F)/文件夹(D)，完成(Enter)，取消(C): ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) break;
                input = input.Trim().ToLowerInvariant();
                if (input == "c") { list.Clear(); break; }
                if (input == "f")
                {
                    try
                    {
                        OpenFileDialog ofd = new OpenFileDialog
                        {
                            Title = "选择文件（可多选）",
                            Filter = "所有文件 (*.*)|*.*",
                            Multiselect = true,
                            RestoreDirectory = true
                        };
                        if (ofd.ShowDialog() == DialogResult.OK && ofd.FileNames?.Length > 0)
                        {
                            list.AddRange(ofd.FileNames);
                            Console.WriteLine("已添加文件: " + ofd.FileNames.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("添加文件出错: " + ex.Message);
                    }
                }
                else if (input == "d")
                {
                    try
                    {
                        FolderBrowserDialog fbd = new FolderBrowserDialog
                        {
                            Description = "选择文件夹",
                            ShowNewFolderButton = true
                        };
                        if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                        {
                            list.Add(fbd.SelectedPath);
                            Console.WriteLine("已添加文件夹: " + fbd.SelectedPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("添加文件夹出错: " + ex.Message);
                    }
                }
            }
            return list;
        }

        // 计算所选内容总大小（文件夹递归）
        static long ComputeTotalSize(System.Collections.Generic.List<string> selections)
        {
            long total = 0;
            foreach (var p in selections)
            {
                if (File.Exists(p))
                {
                    total += new FileInfo(p).Length;
                }
                else if (Directory.Exists(p))
                {
                    try
                    {
                        foreach (var f in Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                            total += new FileInfo(f).Length;
                    }
                    catch { }
                }
            }
            return total;
        }

        // 将所选内容打包为 ZIP（在内存中构建）
        static byte[] BuildZip(System.Collections.Generic.List<string> selections)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true, Encoding.UTF8))
                {
                    var usedTopNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var path in selections)
                    {
                        if (File.Exists(path))
                        {
                            string top = EnsureUniqueTopName(Path.GetFileName(path), usedTopNames);
                            AddFileToZip(zip, path, top);
                        }
                        else if (Directory.Exists(path))
                        {
                            string baseDir = new DirectoryInfo(path).FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            string top = EnsureUniqueTopName(new DirectoryInfo(path).Name, usedTopNames);
                            // 添加空目录入口
                            zip.CreateEntry(top + "/");
                            foreach (var file in Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories))
                            {
                                string rel = file.Substring(baseDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                string entryName = (top + "/" + rel).Replace('\\', '/');
                                AddFileToZip(zip, file, entryName);
                            }
                        }
                    }
                }
                return ms.ToArray();
            }
        }

        static string EnsureUniqueTopName(string name, System.Collections.Generic.HashSet<string> used)
        {
            name = name.Replace('\\', '/');
            if (used.Add(name)) return name;
            string baseName = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            int i = 2;
            while (true)
            {
                string candidate = baseName + " (" + i + ")" + ext;
                if (used.Add(candidate)) return candidate;
                i++;
            }
        }

        static void AddFileToZip(ZipArchive zip, string filePath, string entryName)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var es = entry.Open())
            using (var fs = File.OpenRead(filePath))
            {
                fs.CopyTo(es);
            }
        }

        static void GenerateSelfExtractor(string exePath, byte[] fileBytes, string fileName, string userToken, long timestampTicks)
        {
            // 生成密钥
            byte[] key = DeriveKey(userToken, fileName, fileBytes.Length, timestampTicks);

            // 加密：IV随机，资源内前16字节为IV
            byte[] encryptedCombined = EncryptData(fileBytes, key);

            // 创建临时资源文件来存储加密的二进制数据（IV||Ciphertext）
            string tempDir = Path.GetTempPath();
            string resourceFile = Path.Combine(tempDir, "embedded_file.dat");
            File.WriteAllBytes(resourceFile, encryptedCombined);

            try
            {
                // 将用户token以Base64形式嵌入到生成的EXE（允许为空）
                string tokenB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(userToken ?? string.Empty));

                // 动态生成的 C# 代码模板：新增运行时 Key 校验与覆盖，解密后解压ZIP
                string sourceCode = @"
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;

namespace SelfExtractor
{
    class Program
    {
        // 解密所需常量（编译期写死）
        const string fileName = """ + fileName + @""";
        const int originalFileSize = " + fileBytes.Length + @";
        const long timestampTicks = " + timestampTicks + @"L;
        const string userTokenB64 = """ + tokenB64 + @""";

        // 新增：运行时输入的 Key（若有）
        static string runtimeToken = null;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Console.Title = ""Self-Extractor"";
            }
            catch (Exception ex)
            {
                MessageBox.Show(""初始化错误: "" + ex.Message, ""错误"", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(""=== 自解压工具 ==="");
            Console.WriteLine(""将释放内容包: "" + fileName);
            Console.ResetColor();

            // 若生成时设置了非空 Key，则运行时要求输入并校验；否则直接释放
            string tokenGen = DecodeB64(userTokenB64);
            if (!string.IsNullOrEmpty(tokenGen))
            {
                Console.Write(""请输入 Key 解锁: "");
                string input = Console.ReadLine() ?? string.Empty;
                if (!string.Equals(input, tokenGen, StringComparison.Ordinal))
                {
                    string err = ""Key 不正确，程序退出。"";
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(err);
                    Console.ResetColor();
                    MessageBox.Show(err, ""错误"", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                runtimeToken = input;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(""Key 验证通过。"");
                Console.ResetColor();
            }

            try
            {
                Console.WriteLine();
                Console.WriteLine(""选择释放路径..."");
                string targetDir = SelectOutputFolder();
                
                if (string.IsNullOrEmpty(targetDir))
                {
                    Console.WriteLine(""未选择释放路径，程序退出。"");
                    Console.WriteLine();
                    Console.WriteLine(""按任意键退出..."");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine(""释放路径: "" + targetDir);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(""目标文件夹创建成功: "" + targetDir);
                    Console.ResetColor();
                }

                byte[] fileData = GetEmbeddedFileData();
                ExtractZipBytesToDirectory(fileData, targetDir);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine();
                Console.WriteLine(""内容释放完成。"");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                string errorMsg = ""错误: "" + ex.Message;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(errorMsg);
                Console.ResetColor();
                MessageBox.Show(errorMsg, ""错误"", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Console.WriteLine();
            Console.WriteLine(""按任意键退出..."");
            Console.ReadKey();
        }

        static string DecodeB64(string s)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }

        // 与生成器一致的派生算法（兼容空Key）
        static byte[] DeriveKey(string userToken, string fileName, int fileSize, long timestampTicks)
        {
            string safeToken = userToken ?? string.Empty;
            string material = safeToken + ""|"" + fileName + ""|"" + fileSize.ToString() + ""|"" + timestampTicks.ToString() + ""|SelfExtractor2024"";
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(material));
            }
        }

        // 解密：从资源读取的 combined = IV(16) || CIPHERTEXT
        static byte[] DecryptCombined(byte[] combined, byte[] key)
        {
            if (combined == null || combined.Length < 17) throw new InvalidOperationException(""资源内容不完整"");
            byte[] iv = new byte[16];
            Buffer.BlockCopy(combined, 0, iv, 0, 16);
            int cipherLen = combined.Length - 16;
            byte[] cipher = new byte[cipherLen];
            Buffer.BlockCopy(combined, 16, cipher, 0, cipherLen);

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (MemoryStream ms = new MemoryStream(cipher))
                using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                using (MemoryStream result = new MemoryStream())
                {
                    cs.CopyTo(result);
                    return result.ToArray();
                }
            }
        }

        static string SelectOutputFolder()
        {
            try
            {
                FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
                {
                    Description = ""选择文件释放位置"",
                    ShowNewFolderButton = true
                };

                folderBrowserDialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                
                DialogResult result = folderBrowserDialog.ShowDialog();
                
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
                {
                    return folderBrowserDialog.SelectedPath;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                string errorMsg = ""文件夹选择错误: "" + ex.Message;
                Console.WriteLine(errorMsg);
                MessageBox.Show(errorMsg, ""错误"", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        static byte[] GetEmbeddedFileData()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] resourceNames = assembly.GetManifestResourceNames();
            
            string resourceName = null;
            foreach (string name in resourceNames)
            {
                if (name.EndsWith("".dat""))
                {
                    resourceName = name;
                    break;
                }
            }
            
            if (resourceName == null)
                throw new InvalidOperationException(""无法找到嵌入的文件资源。"");
            
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(""无法读取嵌入的文件资源: "" + resourceName);
                
                byte[] combined = new byte[stream.Length];
                stream.Read(combined, 0, combined.Length);

                // 使用运行时输入的 Key（如有），否则使用编译期写死的 Key
                string token = runtimeToken ?? DecodeB64(userTokenB64);
                byte[] key = DeriveKey(token, fileName, originalFileSize, timestampTicks);
                byte[] decryptedData = DecryptCombined(combined, key);
                return decryptedData;
            }
        }

        static void ExtractZipBytesToDirectory(byte[] zipBytes, string targetDir)
        {
            using (var ms = new MemoryStream(zipBytes))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, false, Encoding.UTF8))
            {
                foreach (var entry in zip.Entries)
                {
                    string fullPath = Path.Combine(targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(fullPath);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    entry.ExtractToFile(fullPath, true);
                }
            }
        }
    }
}";
                // 使用 CodeDom 编译源代码成 EXE
                CSharpCodeProvider provider = new CSharpCodeProvider();
                CompilerParameters parameters = new CompilerParameters
                {
                    GenerateExecutable = true,
                    OutputAssembly = exePath,
                    CompilerOptions = "/target:exe /optimize"
                };
                parameters.ReferencedAssemblies.Add("System.dll");
                parameters.ReferencedAssemblies.Add("System.Core.dll");
                parameters.ReferencedAssemblies.Add("System.Windows.Forms.dll");
                parameters.ReferencedAssemblies.Add("System.IO.Compression.dll");
                parameters.ReferencedAssemblies.Add("System.IO.Compression.FileSystem.dll");
                
                // 将加密的资源文件作为嵌入资源添加
                parameters.EmbeddedResources.Add(resourceFile);

                CompilerResults results = provider.CompileAssemblyFromSource(parameters, sourceCode);
                if (results.Errors.HasErrors)
                {
                    string errorText = "";
                    foreach (CompilerError error in results.Errors)
                    {
                        if (error.IsWarning) continue;
                        errorText += error.ErrorText + "\n";
                    }
                    throw new InvalidOperationException("编译自解压 EXE 失败: " + errorText);
                }
            }
            finally
            {
                // 清理临时资源文件
                try
                {
                    if (File.Exists(resourceFile))
                        File.Delete(resourceFile);
                }
                catch { }
            }
        }
    }
}
