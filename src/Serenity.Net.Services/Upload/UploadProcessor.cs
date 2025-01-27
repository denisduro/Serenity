﻿using Serenity.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using System;
using System.IO;

namespace Serenity.Web
{
    public class UploadProcessor
    {
        private readonly IUploadStorage storage;

        public UploadProcessor(IUploadStorage storage, IExceptionLogger logger = null)
        {
            ThumbBackColor = null;
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Logger = logger;
        }

        public int ThumbWidth { get; set; }
        public int ThumbHeight { get; set; }
        public Color? ThumbBackColor { get; set; }
        public ImageScaleMode ThumbScaleMode { get; set; }
        public int ThumbQuality { get; set; }
        public string ThumbFile { get; private set; }
        public string ThumbUrl { get; private set; }
        public int ImageWidth { get; private set; }
        public int ImageHeight { get; private set; }
        public ImageCheckResult CheckResult { get; private set; }
        public string ErrorMessage { get; private set; }
        public long FileSize { get; private set; }
        public string TemporaryFile { get; private set; }
        public bool IsImage { get; private set; }

        protected IExceptionLogger Logger { get; }

        private bool IsImageExtension(string extension)
        {
            return extension.EndsWith(".jpg") ||
                extension.EndsWith(".jpeg") ||
                extension.EndsWith(".png") ||
                extension.EndsWith(".gif");
        }

        private bool IsDangerousExtension(string extension)
        {
            return extension.EndsWith(".exe") ||
                extension.EndsWith(".bat") ||
                extension.EndsWith(".cmd") ||
                extension.EndsWith(".dll") ||
                extension.EndsWith(".jar") ||
                extension.EndsWith(".jsp") ||
                extension.EndsWith(".htaccess") ||
                extension.EndsWith(".htpasswd") ||
                extension.EndsWith(".lnk") ||
                extension.EndsWith(".vbs") ||
                extension.EndsWith(".vbe") ||
                extension.EndsWith(".aspx") ||
                extension.EndsWith(".ascx") ||
                extension.EndsWith(".config") ||
                extension.EndsWith(".com") ||
                extension.EndsWith(".asmx") ||
                extension.EndsWith(".asax") ||
                extension.EndsWith(".compiled") ||
                extension.EndsWith(".php");
        }

        public bool ProcessStream(Stream fileContent, string extension, 
            ITextLocalizer localizer)
        {
            extension = extension.TrimToEmpty().ToLowerInvariant();
            if (IsDangerousExtension(extension))
            {
                ErrorMessage = "Unsupported file extension!";
                return false;
            }

            CheckResult = ImageCheckResult.InvalidImage;
            ErrorMessage = null;
            ImageWidth = 0;
            ImageHeight = 0;
            IsImage = false;

            var success = false;

            storage.PurgeTemporaryFiles();
            var basePath = "temporary/" + Guid.NewGuid().ToString("N");
            try
            {
                try
                {
                    FileSize = fileContent.Length;
                    fileContent.Seek(0, System.IO.SeekOrigin.Begin);
                    TemporaryFile = storage.WriteFile(basePath + extension, fileContent, false);

                    if (IsImageExtension(extension))
                    {
                        IsImage = true;
                        success = ProcessImageStream(fileContent, localizer);                    
                    }
                    else
                    {
                        success = true;
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage = ex.Message;
                    ex.Log(Logger);
                    success = false;
                    return success;
                }
            }
            finally
            {
                if (!success)
                {
                    if (!ThumbFile.IsNullOrEmpty())
                        storage.DeleteFile(ThumbFile);

                    if (!TemporaryFile.IsNullOrEmpty())
                        storage.DeleteFile(TemporaryFile);
                }

                fileContent.Dispose();
            }

            return success;
        }

        private bool ProcessImageStream(Stream fileContent, ITextLocalizer localizer)
        {
            var imageChecker = new ImageChecker();
            CheckResult = imageChecker.CheckStream(fileContent, true, out Image image, Logger);
            try
            {
                FileSize = imageChecker.DataSize;
                ImageWidth = imageChecker.Width;
                ImageHeight = imageChecker.Height;

                if (CheckResult != ImageCheckResult.JPEGImage &&
                    CheckResult != ImageCheckResult.GIFImage &&
                    CheckResult != ImageCheckResult.PNGImage)
                {
                    ErrorMessage = imageChecker.FormatErrorMessage(CheckResult, localizer);
                    return false;
                }
                else
                {
                    IsImage = true;

                    var extension = CheckResult == ImageCheckResult.PNGImage ? ".png" :
                        (CheckResult == ImageCheckResult.GIFImage ? ".gif" : ".jpg");

                    storage.PurgeTemporaryFiles();

                    if (ThumbWidth > 0 || ThumbHeight > 0)
                    {
                        ThumbnailGenerator.Generate(image, ThumbWidth, ThumbHeight, ThumbScaleMode, ThumbBackColor,
                            inplace: true);
                        var thumbFile = UploadPathHelper.GetThumbnailName(TemporaryFile);

                        using (var ms = new MemoryStream())
                        {
                            if (ThumbQuality != 0)
                                image.Save(ms, new JpegEncoder { Quality = ThumbQuality });
                            else
                                image.Save(ms, new JpegEncoder());
                            ms.Seek(0, SeekOrigin.Begin);
                            ThumbFile = storage.WriteFile(thumbFile, ms, autoRename: false);
                        }
                        ThumbHeight = image.Width;
                        ThumbWidth = image.Height;
                    }

                    return true;
                }
            }
            finally
            {
                if (image != null)
                    image.Dispose();
            }
        }
    }
}
