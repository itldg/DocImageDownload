using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DocImageDownload
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.Title = "VuePress 图片下载工具";
            var command_install = new Command("install", "配置环境变量");
            command_install.SetHandler(() =>
            {
                //自当设置环境变量
                string path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
                if (!path.Contains(ExeDir))
                {
                    Environment.SetEnvironmentVariable("Path", path + ";" + ExeDir, EnvironmentVariableTarget.User);
                    Console.WriteLine("环境变量配置成功");
                }
                else
                {
                    Console.WriteLine("环境变量已配置");
                }
            });
            var command_uninstall = new Command("uninstall", "移除环境变量");
            command_uninstall.SetHandler(() =>
            {
                string path = Environment.GetEnvironmentVariable("Path",EnvironmentVariableTarget.User) ?? "";
                if (path.Contains(ExeDir))
                {
                    Environment.SetEnvironmentVariable("Path", path.Replace(";" + ExeDir, ""), EnvironmentVariableTarget.User);
                    Console.WriteLine("环境变量移除成功");
                }
                else
                {
                    Console.WriteLine("环境变量未配置");
                }
            });

            var doc_dir = new Option<string>(["-dd", "--doc_dir"], () => "src", "MD文档存放文件夹");
            var img_dir = new Option<string>(["-id", "--img_dir"], () => "src\\.vuepress\\public", "图片存放文件夹");
            var no_convert = new Option<bool>(["-nc", "--no_convert"], () => false, "不转换图片格式为 Avif");


            var rootCommand = new RootCommand
            {
                doc_dir,
                img_dir,
                no_convert,
                command_install,
                command_uninstall
            };
            rootCommand.Description = "分析 VuePress 文档中的图片链接并下载,可将图片转码为 Avif 格式,更加节省空间";
            rootCommand.SetHandler(async (doc_dir, img_dir, no_convert) =>
            {
                doc_dir = Path.Combine(Environment.CurrentDirectory, doc_dir);
                if (string.IsNullOrEmpty(doc_dir))
                {
                    Error("文档目录为空");
                }
                if (!Directory.Exists(doc_dir))
                {
                    Error("文档目录不存在,请确认是在文档根目录执行或自行指定文档目录");
                }
                img_dir = Path.Combine(Environment.CurrentDirectory, img_dir);
                if (string.IsNullOrEmpty(img_dir))
                {
                    Error("图片目录为空");
                }
                if (!Directory.Exists(img_dir))
                {
                    Error("图片目录不存在,请确认是在文档根目录执行或自行指定图片目录");
                }
                if (!doc_dir.EndsWith('\\'))
                {
                    doc_dir += "\\";
                }
                if (!img_dir.EndsWith('\\'))
                {
                    img_dir += "\\";
                }
                await DownImgs(doc_dir, img_dir, !no_convert);
            },
            doc_dir, img_dir, no_convert);
            return await rootCommand.InvokeAsync(args);
        }

        static async Task DownImgs(string docDir, string imgDir, bool convertToAvif)
        {
            if (convertToAvif)
            {
                //exe存放的文件夹+ffmpeg.exe
                string ffmpegPath = Path.Combine(ExeDir, "ffmpeg.exe");

                if (!File.Exists(ffmpegPath))
                {
                    string ffmpegZip = Path.Combine(ExeDir, "ffmpeg.zip");
                    Console.WriteLine("首次使用图片转码功能需要下载 FFMpeg ,请稍等片刻 ...");
                    string downloadResult = await DownloadFileAsync("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
                        , ffmpegZip);
                    if (!string.IsNullOrEmpty(downloadResult))
                    {
                        Error("下载 ffmpeg 失败:" + downloadResult + "\r\n如多次下载失败可自行手动下载 ffmpeg.exe 放在软件目录");
                    }
                    bool isOk = false;
                    using (ZipArchive archive = ZipFile.OpenRead(ffmpegZip))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.FullName == "ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe")
                            {
                                entry.ExtractToFile(ffmpegPath, true);
                                isOk = true;
                                break;
                            }
                        }
                    }
                    if (isOk)
                    {
                        File.Delete(ffmpegZip);
                    }
                    else
                    {
                        Error("解压 ffmpeg 失败");
                    }

                }
                FFMpeg.Init(ffmpegPath);
            }
            int imgCount = 0;
            //所有文档文件(md),包含子目录
            string[] docFiles = Directory.GetFiles(docDir, "*.md", SearchOption.AllDirectories);
            int numLength = docFiles.Length.ToString().Length;
            for (int i = 0; i < docFiles.Length; i++)
            {

                Console.WriteLine($"[{(i + 1).ToString().PadLeft(numLength, '0')}/{docFiles.Length}] {docFiles[i].Replace(docDir, "")}");
                string docFile = docFiles[i];
                string? dirName = Path.GetDirectoryName(docFile);
                if (string.IsNullOrEmpty(dirName))
                {
                    //排除空目录
                    continue;
                }
                //获取文档目录,排除设置的文档目录
                string saveImgDirName = $"images\\{dirName.Replace(docDir, "")}";

                string docContent = File.ReadAllText(docFile);
                //匹配图片
                MatchCollection matches = new Regex(@"!\[.*?\]\((.*?)\)").Matches(docContent);
                bool hasImg = false;
                foreach (Match match in matches)
                {
                    string imgUrl = match.Groups[1].Value;
                    if (!imgUrl.StartsWith("http://") && !imgUrl.StartsWith("https://") && !imgUrl.StartsWith("//"))
                    {
                        //不是外链图片跳过
                        continue;
                    }
                    if (imgUrl.StartsWith("//"))
                    {
                        imgUrl = "https:" + imgUrl;
                    }
                    string imgName = Path.GetFileName(imgUrl);
                    imgName = Regex.Replace(imgName, @"(\.\w+)[^\w].*", "$1");
                    string imgFileDir = Path.Combine(imgDir, saveImgDirName);
                    if (!Directory.Exists(imgFileDir))
                    {
                        Directory.CreateDirectory(imgFileDir);
                    }
                    if (imgName.Contains('?'))
                    {
                        imgName = string.Concat(imgName.Substring(0, imgName.IndexOf('?')), ".png");
                    }
                    string imgPath = Path.Combine(imgFileDir, imgName);
                    if (File.Exists(imgPath))
                    {
                        //跳过已存在的图片
                        continue;
                    }
                    string errMsg = await DownloadFileAsync(imgUrl, imgPath);
                    if (!string.IsNullOrEmpty(errMsg))
                    {
                        Console.WriteLine($"下载图片失败:{errMsg}");
                        continue;
                    }
                    if (convertToAvif)
                    {
                        await FFMpeg.Convert(imgPath, imgPath);
                    }
                    //替换文档中的图片路径
                    string imgDocFileName = "/" + Path.Combine(saveImgDirName, imgName).Replace("\\", "/");
                    docContent = docContent.Replace(imgUrl, imgDocFileName);
                    Console.WriteLine($"替换图片路径:{imgUrl}=>{imgDocFileName}");
                    hasImg = true;
                    imgCount++;
                }
                if (hasImg)
                {
                    File.WriteAllText(docFile, docContent);
                }
                else
                {
                    //光标回到上一行
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                    // 用空格覆盖上一行的内容
                    Console.WriteLine(new string(' ', Console.WindowWidth));
                    //光标回到上一行
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                }
            }
            Console.WriteLine($"[{docFiles.Length}/{docFiles.Length}] 文档分析完成,处理了 {imgCount} 张图片");
        }
        /// <summary>
        /// 程序执行错误,打印错误信息,并退出程序
        /// </summary>
        /// <param name="Message">错误提示信息</param>

        static void Error(string Message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(Message);
            Environment.Exit(0);
        }
        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="url">文件地址</param>
        /// <param name="savePath">保存路径</param>
        /// <returns>下载成功返回空,失败返回错误原因</returns>
        static async Task<string> DownloadFileAsync(string url, string savePath)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {

                    HttpResponseMessage response = await client.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        using (var fileStream = File.Create(savePath))
                        {
                            await response.Content.CopyToAsync(fileStream);
                            return "";
                        }
                    }
                    else
                    {
                        return $"Failed to download file. Status code: {response.StatusCode}";
                    }
                }

            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
        static string _ExeDir = "";
        /// <summary>
        /// 当前进程所在目录
        /// </summary>
        static string ExeDir
        {
            get
            {
                if (!string.IsNullOrEmpty(_ExeDir))
                {
                    return _ExeDir;
                }
                return _ExeDir = Path.GetDirectoryName(Environment.ProcessPath!)!;
            }
        }
    }

}
