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

namespace SelfExtractGenerator
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.Title = "Self-Extract Generator";

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== 自解压文件生成器 v1.0 ===");
            Console.ResetColor();

            try
            {
                // 计算最大文件大小基于可用内存
                long maxFileSize = GetMaxFileSizeBasedOnMemory();
                Console.WriteLine("\n系统可用内存约: " + (maxFileSize / (1024 * 1024)) + " MB");
                Console.WriteLine("最大支持文件大小: " + (maxFileSize / (1024 * 1024)) + " MB (基于80%可用内存)");
                Console.ResetColor();

                // 步骤1: 选择源文件
                Console.WriteLine("\n步骤1: 选择源文件");
                string sourceFile = SelectSourceFile();
                if (string.IsNullOrEmpty(sourceFile))
                {
                    Console.WriteLine("未选择源文件，程序退出。");
                    Console.WriteLine("\n按任意键退出...");
                    Console.ReadKey();
                    return;
                }
                FileInfo fileInfo = new FileInfo(sourceFile);
                if (fileInfo.Length > maxFileSize)
                {
                    throw new InvalidOperationException("源文件过大（" + (fileInfo.Length / (1024 * 1024)) + " MB），超过系统可用内存限制 (" + (maxFileSize / (1024 * 1024)) + " MB)。请选择较小文件。");
                }

                byte[] fileBytes = File.ReadAllBytes(sourceFile);
                string fileName = Path.GetFileName(sourceFile);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("源文件加载成功: " + fileName + " (" + fileBytes.Length + " 字节)");
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

                // 步骤3: 生成自解压 EXE
                string exeName = "SelfExtractor_" + Path.GetFileNameWithoutExtension(fileName) + ".exe";
                string exePath = Path.Combine(outputDir, exeName);
                GenerateSelfExtractor(exePath, fileBytes, fileName);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n自解压 EXE 生成成功: " + exePath);
                Console.WriteLine("运行该 EXE 时，它会提示指定释放路径，然后提取原文件。");
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
                return (long)(availableMB * 1024 * 1024 * 0.8); // 80% of available memory
            }
            catch
            {
                // 如果PerformanceCounter失败，回退到默认值（例如1GB）
                return 1024L * 1024 * 1024;
            }
        }

        // AES对称加密
        static byte[] EncryptData(byte[] data, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(data, 0, data.Length);
                    }
                    return ms.ToArray();
                }
            }
        }

        // 生成复杂的加密密钥和IV
        static void GenerateKeyAndIV(string fileName, int fileSize, out byte[] key, out byte[] iv)
        {
            // 使用固定的时间戳以确保加密解密一致性
            long fixedTimestamp = 638672000000000000L; // 固定时间戳
            
            // 使用复杂的基础字符串生成密钥
            string complexToken = "SelfExtractor2024_Advanced_Encryption_Token_" + 
                                 "9A7B3F2E8D6C5A4B1E9F7D3C8A6B4E2F5A9D7C3B6E8F1A4C7B9E2D5F8A3C6B9E" +
                                 "_ComplexSalt_" + fileName + "_Size_" + fileSize.ToString() + 
                                 "_Timestamp_" + fixedTimestamp.ToString() +
                                 "_RandomSeed_7F3E9A2D5C8B1F4A6E9D3B7C2A5E8F1B4D7A3C6E9F2B5A8D1C4F7B3E6A9D2C5F8";

            // 使用SHA256生成密钥
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(complexToken));
                key = hashBytes; // SHA256 produces 32 bytes, perfect for AES-256
            }

            // 为IV生成不同的哈希
            string ivSource = complexToken + "_IV_Salt_" + "F3A7B2E9D5C1A8F4B6E2D9C3A7F1B5E8D2C6A9F3B7E1C4A8D5F2B9E6C3A7F1B4";
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] ivHashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(ivSource));
                iv = new byte[16]; // AES IV is 16 bytes
                Array.Copy(ivHashBytes, 0, iv, 0, 16);
            }
        }

        static void GenerateSelfExtractor(string exePath, byte[] fileBytes, string fileName)
        {
            // 生成加密密钥和IV
            byte[] key, iv;
            GenerateKeyAndIV(fileName, fileBytes.Length, out key, out iv);
            
            // 加密文件数据
            byte[] encryptedBytes = EncryptData(fileBytes, key, iv);
            
            // 创建临时资源文件来存储加密的二进制数据
            string tempDir = Path.GetTempPath();
            string resourceFile = Path.Combine(tempDir, "embedded_file.dat");
            File.WriteAllBytes(resourceFile, encryptedBytes);

            try
            {
                // 动态生成的 C# 代码模板，自解压 EXE 的源代码
                string sourceCode = @"
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;

namespace SelfExtractor
{
    class Program
    {
        const string fileName = """ + fileName + @""";
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
            Console.WriteLine(""将释放文件: "" + fileName);
            Console.ResetColor();

            try
            {
                Console.WriteLine(""\n选择释放路径..."");
                string targetDir = SelectOutputFolder();
                
                if (string.IsNullOrEmpty(targetDir))
                {
                    Console.WriteLine(""未选择释放路径，程序退出。"");
                    Console.WriteLine(""\n按任意键退出..."");
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

                string targetFile = Path.Combine(targetDir, fileName);
                byte[] fileData = GetEmbeddedFileData();
                
                File.WriteAllBytes(targetFile, fileData);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(""\n文件释放成功: "" + targetFile);
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

            Console.WriteLine(""\n按任意键退出..."");
            Console.ReadKey();
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

        static byte[] DecryptData(byte[] encryptedData, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (MemoryStream ms = new MemoryStream(encryptedData))
                {
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        using (MemoryStream result = new MemoryStream())
                        {
                            cs.CopyTo(result);
                            return result.ToArray();
                        }
                    }
                }
            }
        }

        static void GenerateKeyAndIV(string fileName, int originalFileSize, out byte[] key, out byte[] iv)
        {
            long fixedTimestamp = 638672000000000000L;
            
            string complexToken = ""SelfExtractor2024_Advanced_Encryption_Token_"" + 
                                 ""9A7B3F2E8D6C5A4B1E9F7D3C8A6B4E2F5A9D7C3B6E8F1A4C7B9E2D5F8A3C6B9E"" +
                                 ""_ComplexSalt_"" + fileName + ""_Size_"" + originalFileSize.ToString() + 
                                 ""_Timestamp_"" + fixedTimestamp.ToString() +
                                 ""_RandomSeed_7F3E9A2D5C8B1F4A6E9D3B7C2A5E8F1B4D7A3C6E9F2B5A8D1C4F7B3E6A9D2C5F8"";

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(complexToken));
                key = hashBytes;
            }

            string ivSource = complexToken + ""_IV_Salt_"" + ""F3A7B2E9D5C1A8F4B6E2D9C3A7F1B5E8D2C6A9F3B7E1C4A8D5F2B9E6C3A7F1B4"";
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] ivHashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(ivSource));
                iv = new byte[16];
                Array.Copy(ivHashBytes, 0, iv, 0, 16);
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
                
                byte[] encryptedBuffer = new byte[stream.Length];
                stream.Read(encryptedBuffer, 0, encryptedBuffer.Length);
                
                int originalFileSize = " + fileBytes.Length + @";
                byte[] key, iv;
                GenerateKeyAndIV(fileName, originalFileSize, out key, out iv);
                byte[] decryptedData = DecryptData(encryptedBuffer, key, iv);
                
                return decryptedData;
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
