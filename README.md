# BDpan-NoExtract

## 项目简介 | Project Description
本程序可以将你要分享的文件转换为自解压可执行文件（`.exe`），然后再上传到你要分享的平台。  
这样可以防止平台通过文件 **Hash 校验** 来封禁分享链接，从而保证文件能够顺利分享。  

This tool converts the files you want to share into a self-extracting executable (`.exe`) before uploading them to the target platform.  
By doing so, it prevents the platform from blocking your shared link through **hash-based inspection**, ensuring successful file sharing.  

## 使用方法 | Usage
1. 选择你要分享的文件或压缩包  
2. 使用本程序生成自解压 `.exe`  
3. 上传生成的 `.exe` 到目标平台，而不是原始文件  

1. Select the file or archive you want to share  
2. Run this program to generate a self-extracting `.exe`  
3. Upload the generated `.exe` to the target platform instead of the original file  

---
## 注意事项 | Notes
#由于我没签名，没钱，所以创建携带密码的exe时可能被WD报毒拦截！！！
由于是自解压 `.exe`，网盘无法执行以查看其中的文件。  
但需要注意的是，该文件只能在 **Windows 10/11 或其他支持 .NET Framework 4.0 的设备** 上打开查看。  

Since the file is packaged as a self-extracting `.exe`, the cloud storage platform cannot execute it to view the contents.  
However, the limitation is that it can only be opened on **Windows 10/11 or other devices that support .NET Framework 4.0**.

## 发行版 | Releases
编译好的程序可在 [Releases](../../releases) 页面下载。  

The pre-built binaries are available on the [Releases](../../releases) page.  

---

