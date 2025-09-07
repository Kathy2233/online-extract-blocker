using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Reflection;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using System.Windows.Forms;

namespace SelfExtractGenerator
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.Title = "Self-Extract Generator";
            if (!IsRunAsAdmin())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("检测到非管理员权限。尝试提升权限...");
                Console.ResetColor();
                if (!ElevatePrivileges())
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("权限提升失败。请以管理员身份手动运行此程序。");
                    Console.ResetColor();
                    Console.WriteLine("按任意键退出...");
                    Console.ReadKey();
                    return;
                }
                return; // 如果提升成功，进程会退出，原进程被新进程替换
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== 自解压文件生成器 v1.0 ===");
            Console.WriteLine("运行于管理员权限。");
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

        static bool IsRunAsAdmin()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static bool ElevatePrivileges()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = Assembly.GetExecutingAssembly().Location,
                    Verb = "runas",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                return true;
            }
            catch
            {
                return false;
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

        static void GenerateSelfExtractor(string exePath, byte[] fileBytes, string fileName)
        {
            // 创建临时资源文件来存储二进制数据
            string tempDir = Path.GetTempPath();
            string resourceFile = Path.Combine(tempDir, "embedded_file.dat");
            File.WriteAllBytes(resourceFile, fileBytes);

            try
            {
                // 动态生成的 C# 代码模板，自解压 EXE 的源代码
                string sourceCode = @"
using System;
using System.IO;
using System.Security.Principal;
using System.Reflection;
using System.Diagnostics;
using System.Windows.Forms;

namespace SelfExtractor
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                // 初始化 Windows Forms 应用程序
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                Console.Title = ""Self-Extractor"";
                
                // 写入调试日志
                WriteDebugLog(""程序启动"");
                
                if (!IsRunAsAdmin())
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(""建议以管理员身份运行以避免权限问题。"");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog(""初始化错误: "" + ex.Message);
                MessageBox.Show(""初始化错误: "" + ex.Message, ""错误"", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(""=== 自解压工具 ==="");
            Console.WriteLine(""将释放文件: " + fileName + @""");
            Console.ResetColor();

            try
            {
                WriteDebugLog(""开始选择释放路径"");
                Console.WriteLine(""\n选择释放路径..."");
                string targetDir = SelectOutputFolder();
                WriteDebugLog(""选择的路径: "" + (targetDir ?? ""null""));
                
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

                WriteDebugLog(""开始提取文件数据"");
                string targetFile = Path.Combine(targetDir, """ + fileName + @""");
                byte[] fileData = GetEmbeddedFileData();
                WriteDebugLog(""文件数据大小: "" + fileData.Length + "" bytes"");
                
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
                WriteDebugLog(errorMsg);
                MessageBox.Show(errorMsg, ""错误"", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Console.WriteLine(""\n按任意键退出..."");
            Console.ReadKey();
        }

        static string SelectOutputFolder()
        {
            try
            {
                WriteDebugLog(""进入文件夹选择对话框"");
                FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
                {
                    Description = ""选择文件释放位置"",
                    ShowNewFolderButton = true
                };

                // 设置初始路径为桌面
                folderBrowserDialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                
                WriteDebugLog(""显示文件夹选择对话框"");
                DialogResult result = folderBrowserDialog.ShowDialog();
                WriteDebugLog(""对话框结果: "" + result.ToString());
                
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
                {
                    WriteDebugLog(""选择的路径: "" + folderBrowserDialog.SelectedPath);
                    return folderBrowserDialog.SelectedPath;
                }
                
                WriteDebugLog(""未选择路径或取消"");
                return null;
            }
            catch (Exception ex)
            {
                string errorMsg = ""文件夹选择错误: "" + ex.Message;
                WriteDebugLog(errorMsg);
                Console.WriteLine(errorMsg);
                MessageBox.Show(errorMsg, ""错误"", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        static bool IsRunAsAdmin()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static byte[] GetEmbeddedFileData()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] resourceNames = assembly.GetManifestResourceNames();
            
            // 查找第一个.dat资源文件
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
                throw new InvalidOperationException(""无法找到嵌入的文件资源。可用资源: "" + string.Join("", "", resourceNames));
            
            WriteDebugLog(""找到资源: "" + resourceName);
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(""无法读取嵌入的文件资源: "" + resourceName);
                
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                WriteDebugLog(""成功读取 "" + buffer.Length + "" bytes 数据"");
                return buffer;
            }
        }
        
        static void WriteDebugLog(string message)
        {
            try
            {
                string logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ""SelfExtractor_Debug.log"");
                string logEntry = DateTime.Now.ToString(""yyyy-MM-dd HH:mm:ss"") + "" - "" + message + Environment.NewLine;
                File.AppendAllText(logFile, logEntry);
            }
            catch
            {
                // 忽略日志写入错误
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
                
                // 将资源文件作为嵌入资源添加
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
