﻿using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ReactiveUI;
using ServerProxy.Tools;

namespace ServerProxy.ViewModels;

public class AppViewModel : ViewModelBase, INotifyPropertyChanged
{
    private static Config _appConfig;

    private string _statusMessage = "启动中";

    public AppViewModel(ThemeVariant variant)
    {
        // Read config
        var rawConf = "{}";
        try
        {
            rawConf = File.ReadAllText(Path.GetDirectoryName(Environment.ProcessPath) + "/config.json");
        }
        catch (Exception ex)
        {
            // ignored
        }
        finally
        {
            _appConfig = JsonSerializer.Deserialize(rawConf, SourceGenerationContext.Default.Config);
            _appConfig ??= new Config();
        }

        RenderIcon(variant);
    }

    public Bitmap? CheckIcon { get; set; }
    public Bitmap? CheckUpdateCheckIcon => _appConfig.CheckUpdate ? CheckIcon : null;
    public Bitmap? ShowMessageBoxOnStartCheckIcon => _appConfig.ShowMessageBoxOnStart ? CheckIcon : null;
    public Bitmap? ShowDebugConsoleOnStartCheckIcon => _appConfig.ShowDebugConsoleOnStart ? CheckIcon : null;
    public Bitmap? EnableSystemProxyCheckIcon => _appConfig.EnableSystemProxy ? CheckIcon : null;

    public bool CheckUpdate
    {
        get => _appConfig.CheckUpdate;
        set
        {
            _appConfig.CheckUpdate = value;
            this.RaisePropertyChanged();
            NotifyPropertyChanged(nameof(CheckUpdateCheckIcon));
        }
    }

    public bool ShowMessageBoxOnStart
    {
        get => _appConfig.ShowMessageBoxOnStart;
        set
        {
            _appConfig.ShowMessageBoxOnStart = value;
            this.RaisePropertyChanged();
            NotifyPropertyChanged(nameof(ShowMessageBoxOnStartCheckIcon));
        }
    }

    public bool ShowDebugConsoleOnStart
    {
        get => _appConfig.ShowDebugConsoleOnStart;
        set
        {
            _appConfig.ShowDebugConsoleOnStart = value;
            this.RaisePropertyChanged();
            NotifyPropertyChanged(nameof(ShowDebugConsoleOnStartCheckIcon));
        }
    }

    public bool EnableSystemProxy
    {
        get => _appConfig.EnableSystemProxy;
        set
        {
            int fails = 0;
            if (value == true)
            {
                if (!SysProxyHelper.TrySetSysProxy($"127.0.0.1:{_appConfig.ListeningPort}")) {
                    fails++;
                }
            } else {
                SysProxyHelper.UnsetSysProxy();
            }

            if(fails < 1)
            {
                _appConfig.EnableSystemProxy = value;
                this.RaisePropertyChanged();
                NotifyPropertyChanged(nameof(EnableSystemProxyCheckIcon));
            }
        }
    }

    public Config AppConfig
    {
        get => _appConfig;
        set => _appConfig = value;
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            NotifyPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private static Bitmap? CreateBitmap(StreamGeometry streamGeometry, IImmutableSolidColorBrush brush)
    {
        var internalImage = new DrawingImage
        {
            Drawing = new GeometryDrawing
            {
                Geometry = streamGeometry,
                Brush = brush
            }
        };
        var pixelSize = new PixelSize((int)internalImage.Size.Width, (int)internalImage.Size.Height);
        Bitmap? returnImage = null;

        using (MemoryStream memoryStream = new())
        {
            using (RenderTargetBitmap bitmap = new(pixelSize, new Vector(60, 60)))
            {
                using (var ctx = bitmap.CreateDrawingContext())
                {
                    internalImage.Drawing.Draw(ctx);
                }

                bitmap.Save(memoryStream);
            }

            memoryStream.Seek(0, SeekOrigin.Begin);
            returnImage = new Bitmap(memoryStream);
        }

        return returnImage;
    }

    public void RenderIcon(ThemeVariant variant)
    {
        CheckIcon = CreateBitmap((StreamGeometry)Application.Current.FindResource("CheckmarkRegular"),
            variant == ThemeVariant.Dark ? Brushes.WhiteSmoke : Brushes.Black);
        NotifyPropertyChanged(nameof(CheckUpdateCheckIcon));
        NotifyPropertyChanged(nameof(ShowMessageBoxOnStartCheckIcon));
        NotifyPropertyChanged(nameof(ShowDebugConsoleOnStartCheckIcon));
        NotifyPropertyChanged(nameof(EnableSystemProxyCheckIcon));
    }

    // This method is called by the Set accessor of each property.  
    // The CallerMemberName attribute that is applied to the optional propertyName  
    // parameter causes the property name of the caller to be substituted as an argument.  
    private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}