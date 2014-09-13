﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.Common
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public sealed class GdbSetup : IDisposable
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public GdbSetup (AndroidProcess process, string gdbToolPath)
    {
      LoggingUtils.PrintFunction ();

      Process = process;

      Host = "localhost";

      Port = 5039;

      if (!Process.HostDevice.IsOverWiFi)
      {
        Socket = "debug-socket";
      }

      string deviceDir = Process.HostDevice.ID.Replace (':', '-');

      CacheDirectory = string.Format (@"{0}\Android++\Cache\{1}\{2}", Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), deviceDir, Process.Name);

      Directory.CreateDirectory (CacheDirectory);

      CacheSysRoot = Path.Combine (CacheDirectory, "sysroot");

      Directory.CreateDirectory (CacheSysRoot);

      SymbolDirectories = new List<string> ();

      GdbToolPath = gdbToolPath;

      GdbToolArguments = "--interpreter=mi ";

      if (!File.Exists (gdbToolPath))
      {
        throw new FileNotFoundException ("Could not find requested GDB instance. Expected: " + gdbToolPath);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose ()
    {
      LoggingUtils.PrintFunction ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public AndroidProcess Process { get; set; }

    public string Socket { get; set; }

    public string Host { get; set; }

    public uint Port { get; set; }

    public string CacheDirectory { get; set; }

    public string CacheSysRoot { get; set; }

    public List<string> SymbolDirectories { get; set; }

    public string GdbToolPath { get; set; }

    public string GdbToolArguments { get; set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void SetupPortForwarding ()
    {
      // 
      // Setup network redirection.
      // 

      LoggingUtils.PrintFunction ();

      StringBuilder commandLineArgumentsBuilder = new StringBuilder ();

      commandLineArgumentsBuilder.AppendFormat ("tcp:{0} ", Port);

      if (!string.IsNullOrWhiteSpace (Socket))
      {
        commandLineArgumentsBuilder.AppendFormat ("localfilesystem:{0}/{1}", Process.InternalCacheDirectory, Socket);
      }
      else
      {
        commandLineArgumentsBuilder.AppendFormat ("tcp:{0} ", Port);
      }

      using (SyncRedirectProcess adbPortForward = AndroidAdb.AdbCommand (Process.HostDevice, "forward", commandLineArgumentsBuilder.ToString ()))
      {
        adbPortForward.StartAndWaitForExit ();
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void ClearPortForwarding ()
    {
      // 
      // Clear network redirection.
      // 

      LoggingUtils.PrintFunction ();

      using (SyncRedirectProcess adbPortForward = AndroidAdb.AdbCommand (Process.HostDevice, "forward", "--remove-all"))
      {
        adbPortForward.StartAndWaitForExit (1000);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public string [] CacheSystemBinaries ()
    {
      // 
      // Pull the required binaries from the device.
      // 

      LoggingUtils.PrintFunction ();

      List<string> deviceBinaries = new List<string> ();

      string [] remoteBinaries = 
      {
        "/system/bin/app_process",
        "/system/bin/app_process32",
        "/system/bin/app_process64",
        "/system/bin/linker",
        "/system/lib/libandroid.so",
        "/system/lib/libandroid_runtime.so",
        "/system/lib/libart.so",
        "/system/lib/libbinder.so",
        "/system/lib/libc.so",
        "/system/lib/libdvm.so",
        "/system/lib/libEGL.so",
        "/system/lib/libGLESv1_CM.so",
        "/system/lib/libGLESv2.so",
        //"/system/lib/libGLESv3.so"
        "/system/lib/libutils.so",
      };

      foreach (string binary in remoteBinaries)
      {
        string cachedBinary = Path.Combine (CacheSysRoot, binary.Substring (1));

        string cahedBinaryFullPath = Path.Combine (Path.GetDirectoryName (cachedBinary), Path.GetFileName (cachedBinary));

        Directory.CreateDirectory (Path.GetDirectoryName (cahedBinaryFullPath));

        if (File.Exists (cahedBinaryFullPath))
        {
          deviceBinaries.Add (cahedBinaryFullPath);

          LoggingUtils.Print (string.Format ("[GdbSetup] Using cached {0}.", binary));
        }
        else
        {
          try
          {
            Process.HostDevice.Pull (binary, cachedBinary);

            deviceBinaries.Add (cahedBinaryFullPath);

            LoggingUtils.Print (string.Format ("[GdbSetup] Pulled {0} from device/emulator.", binary));
          }
          catch (Exception e)
          {
            LoggingUtils.HandleException (e);
          }
        }
      }

      return deviceBinaries.ToArray ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public string [] CacheApplicationBinaries ()
    {
      // 
      // Application binaries (those under /lib/ of an installed application).
      // TODO: Consider improving this. Pulling libraries ensures consistency, but takes time (ADB is a slow protocol).
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        string libraryCachePath = Path.Combine (CacheSysRoot, Process.InternalNativeLibrariesDirectory.Substring (1));

        Directory.CreateDirectory (libraryCachePath);

        if (Process.HostDevice.SdkVersion == AndroidSettings.VersionCode.L_PREVIEW)
        {
          // 
          // On Android L, Google have broken pull permissions to 'app-lib' content so we use cat to avoid this.
          // 

          string [] libraries = Process.HostDevice.Shell ("ls", Process.InternalNativeLibrariesDirectory).Replace ("\r", "").Split (new char [] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

          foreach (string lib in libraries)
          {
            string remoteLib = Process.InternalNativeLibrariesDirectory + "/" + lib;

            string temporaryStorage = "/data/local/tmp/" + lib;

            Process.HostDevice.Shell ("cat", string.Format ("{0} > {1}", remoteLib, temporaryStorage));

            Process.HostDevice.Pull (temporaryStorage, libraryCachePath);

            Process.HostDevice.Shell ("rm", temporaryStorage);
          }
        }
        else
        {
          Process.HostDevice.Pull (Process.InternalNativeLibrariesDirectory, libraryCachePath);
        }

        LoggingUtils.Print (string.Format ("[GdbSetup] Pulled application libraries from device/emulator."));

        string [] additionalLibraries = Directory.GetFiles (libraryCachePath, "lib*.so", SearchOption.AllDirectories);

        return additionalLibraries;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }

      return new string [] {};
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public string [] CreateGdbExecutionScript ()
    {
      LoggingUtils.PrintFunction ();

      List<string> gdbExecutionCommands = new List<string> ();

      gdbExecutionCommands.Add ("set target-async on");

      //gdbExecutionCommands.Add ("set mi-async on"); // as above, from GDB 7.7

      gdbExecutionCommands.Add ("set breakpoint pending on");

      gdbExecutionCommands.Add ("set logging file " + PathUtils.SantiseWindowsPath (Path.Combine (CacheDirectory, "gdb.log")));

      gdbExecutionCommands.Add ("set logging overwrite on");

      gdbExecutionCommands.Add ("set logging on");

#if DEBUG && false
      gdbExecutionCommands.Add ("set debug remote 1");

      gdbExecutionCommands.Add ("set debug infrun 1");

      gdbExecutionCommands.Add ("set verbose on");
#endif

      return gdbExecutionCommands.ToArray ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  }

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
