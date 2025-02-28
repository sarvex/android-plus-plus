﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

using AndroidPlusPlus.Common;
using AndroidPlusPlus.VsDebugCommon;

using Microsoft.VisualStudio.Debugger.Interop;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.VsDebugEngine
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public class CLangDebugger : IDisposable
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public delegate void InterruptOperation (CLangDebugger debugger);

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private GdbSetup m_gdbSetup = null;

    private int m_interruptOperationCounter = 0;

    private ManualResetEvent m_interruptOperationCompleted = new ManualResetEvent (false);

    private Dictionary<string, uint> m_threadGroupStatus = new Dictionary<string, uint> ();

    private Dictionary<string, Tuple<ulong, ulong, bool>> m_mappedSharedLibraries = new Dictionary<string, Tuple<ulong, ulong, bool>> ();

    private readonly LaunchConfiguration m_launchConfiguration;

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebugger (DebugEngine debugEngine, LaunchConfiguration launchConfiguration, DebuggeeProgram debugProgram)
    {
      Engine = debugEngine;

      m_launchConfiguration = launchConfiguration;

      NativeProgram = new CLangDebuggeeProgram (this, debugProgram);

      NativeMemoryBytes = new CLangDebuggeeMemoryBytes (this);

      VariableManager = new CLangDebuggerVariableManager (this);

      // 
      // Evaluate target device's architecture triple.
      // 

      string preferedGdbAbiToolPrefix = string.Empty;

      bool allow64BitAbis = true;

      switch (debugProgram.DebugProcess.NativeProcess.PrimaryCpuAbi)
      {
        case "armeabi":
        case "armeabi-v7a":
        {
          preferedGdbAbiToolPrefix = "arm-linux-androideabi";

          break;
        }

        case "arm64-v8a":
        {
          if (allow64BitAbis)
          {
            preferedGdbAbiToolPrefix = "aarch64-linux-android";
          }
          else
          {
            preferedGdbAbiToolPrefix = "arm-linux-androideabi";
          }

          break;
        }

        case "x86":
        {
          preferedGdbAbiToolPrefix = "i686-linux-android";

          break;
        }

        case "x86_64":
        {
          if (allow64BitAbis)
          {
            preferedGdbAbiToolPrefix = "x86_64-linux-android";
          }
          else
          {
            preferedGdbAbiToolPrefix = "i686-linux-android";
          }

          break;
        }

        case "mips":
        {
          preferedGdbAbiToolPrefix = "mipsel-linux-android";

          break;
        }

        case "mips64":
        {
          if (allow64BitAbis)
          {
            preferedGdbAbiToolPrefix = "mips64el-linux-android";
          }
          else
          {
            preferedGdbAbiToolPrefix = "mipsel-linux-android";
          }

          break;
        }
      }

      if (string.IsNullOrEmpty (preferedGdbAbiToolPrefix))
      {
        throw new InvalidOperationException (string.Format ("Unrecognised target primary CPU ABI: {0}", debugProgram.DebugProcess.NativeProcess.PrimaryCpuAbi));
      }

      bool preferedGdbAbiIs64Bit = preferedGdbAbiToolPrefix.Contains ("64");

      Engine.Broadcast (new DebugEngineEvent.DebuggerConnectionEvent (DebugEngineEvent.DebuggerConnectionEvent.EventType.LogStatus, string.Format ("Configuring GDB for '{0}' target...", preferedGdbAbiToolPrefix)), null, null);

      // 
      // Android++ bundles its own copies of GDB to get round various NDK issues. Search for these.
      // 

      string androidPlusPlusRoot = Environment.GetEnvironmentVariable ("ANDROID_PLUS_PLUS");

      // 
      // Build GDB version permutations.
      // 

      List<string> gdbToolPermutations = new List<string> ();

      string [] availableHostArchitectures = new string [] { "x86", "x86_64" };

      foreach (string arch in availableHostArchitectures)
      {
        if (arch.Contains ("64") && !Environment.Is64BitOperatingSystem)
        {
          continue;
        }

        string gdbToolFilePattern = string.Format ("{0}-gdb.cmd", preferedGdbAbiToolPrefix);

        string [] availableVersionPaths = Directory.GetDirectories (Path.Combine (androidPlusPlusRoot, "contrib", "gdb", "bin", arch), "*.*.*", SearchOption.TopDirectoryOnly);

        foreach (string versionPath in availableVersionPaths)
        {
          string [] gdbToolMatches = Directory.GetFiles (versionPath, gdbToolFilePattern, SearchOption.TopDirectoryOnly);

          foreach (string tool in gdbToolMatches)
          {
            gdbToolPermutations.Add (tool);
          }
        }
      }

      if (gdbToolPermutations.Count == 0)
      {
        throw new InvalidOperationException ("Could not locate required 32/64-bit GDB deployments.");
      }
      else
      {
        // 
        // Pick the oldest GDB version available if running 'Jelly Bean' or below.
        // 

        bool forceNdkR9dClient = (debugProgram.DebugProcess.NativeProcess.HostDevice.SdkVersion <= AndroidSettings.VersionCode.JELLY_BEAN);

        if (forceNdkR9dClient)
        {
          m_gdbSetup = new GdbSetup (debugProgram.DebugProcess.NativeProcess, gdbToolPermutations [0]);
        }
        else
        {
          m_gdbSetup = new GdbSetup (debugProgram.DebugProcess.NativeProcess, gdbToolPermutations [gdbToolPermutations.Count - 1]);
        }

        // 
        // A symbolic link to 'share' is placed in the architecture directory, provide GDB with that location.
        // 

        string architecturePath = Path.GetDirectoryName (Path.GetDirectoryName (m_gdbSetup.GdbToolPath));

        string pythonGdbScriptsPath = Path.Combine (architecturePath, "share", "gdb");

        m_gdbSetup.GdbToolArguments += " --data-directory " + PathUtils.SantiseWindowsPath (pythonGdbScriptsPath);
      }

      if (m_gdbSetup == null)
      {
        throw new InvalidOperationException ("Could not evaluate a suitable GDB instance. Ensure you have the correct NDK deployment for your system's architecture.");
      }

      if (m_launchConfiguration != null)
      {
        string launchDirectory;

        if (m_launchConfiguration.TryGetValue ("LaunchSuspendedDir", out launchDirectory))
        {
          m_gdbSetup.SymbolDirectories.Add (launchDirectory);
        }
      }

      GdbServer = new GdbServer (m_gdbSetup);

      GdbClient = new GdbClient (m_gdbSetup);

      GdbClient.OnResultRecord = OnClientResultRecord;

      GdbClient.OnAsyncRecord = OnClientAsyncRecord;

      GdbClient.OnStreamRecord = OnClientStreamRecord;

      GdbClient.Start ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Kill ()
    {
      LoggingUtils.PrintFunction ();

      if (GdbClient != null)
      {
        GdbClient.Kill ();
      }

      if (GdbServer != null)
      {
        GdbServer.Kill ();
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose ()
    {
      Dispose (true);

      GC.SuppressFinalize (this);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected virtual void Dispose (bool disposing)
    {
      if (disposing)
      {
        if (GdbClient != null)
        {
          GdbClient.Dispose ();

          GdbClient = null;
        }

        if (GdbServer != null)
        {
          GdbServer.Dispose ();

          GdbServer = null;
        }

        if (m_gdbSetup != null)
        {
          m_gdbSetup.Dispose ();

          m_gdbSetup = null;
        }

        if (m_interruptOperationCompleted != null)
        {
          m_interruptOperationCompleted.Dispose ();

          m_interruptOperationCompleted = null;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public GdbServer GdbServer { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public GdbClient GdbClient { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public DebugEngine Engine { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeProgram NativeProgram { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeMemoryBytes NativeMemoryBytes { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggerVariableManager VariableManager { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void RunInterruptOperation (InterruptOperation operation, bool shouldContinue = true)
    {
      // 
      // Interrupt the GDB session in order to execute the provided delegate in a 'stopped' state.
      // 

      LoggingUtils.PrintFunction ();

      if (operation == null)
      {
        throw new ArgumentNullException ("operation");
      }

      if (GdbClient == null)
      {
        throw new InvalidOperationException ("Can not perform interrupt; GdbClient is not present");
      }

      bool targetWasRunning = false;

      try
      {
        lock (this)
        {
          lock (NativeProgram)
          {
            targetWasRunning = NativeProgram.IsRunning;
          }

          if ((Interlocked.Increment (ref m_interruptOperationCounter) == 1) && targetWasRunning)
          {
            LoggingUtils.Print ("[CLangDebugger] RunInterruptOperation: Issuing interrupt.");

            m_interruptOperationCompleted.Reset ();

            GdbClient.Stop ();
          }
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }

      try
      {
        lock (this)
        {
          bool interruptSignalled = false;

          lock (NativeProgram)
          {
            interruptSignalled = !NativeProgram.IsRunning;
          }

          while (!interruptSignalled)
          {
            LoggingUtils.Print ("[CLangDebugger] RunInterruptOperation: Waiting for interrupt to stop target.");

            interruptSignalled = m_interruptOperationCompleted.WaitOne (0);

            if (!interruptSignalled)
            {
              Application.DoEvents ();

              Thread.Sleep (100);
            }
          }

          operation (this);
        }
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        throw; // Allows the caller of RunInterruptOperation to be informed of operation() failures.
      }
      finally
      {
        try
        {
          lock (this)
          {
            if ((Interlocked.Decrement (ref m_interruptOperationCounter) == 0) && targetWasRunning && shouldContinue)
            {
              LoggingUtils.Print ("[CLangDebugger] RunInterruptOperation: Returning target to running state.");

              GdbClient.Continue ();
            }
          }
        }
        catch (Exception e)
        {
          LoggingUtils.HandleException (e);
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void OnClientResultRecord (MiResultRecord resultRecord)
    {
      LoggingUtils.PrintFunction ();

      switch (resultRecord.Class)
      {
        case "done":
        case "running": // same behaviour (backward compatibility)
        {
          // 
          // "^done" [ "," results ]: The synchronous operation was successful, results are the return values.
          // 

          try
          {
            if (resultRecord.HasField ("reason"))
            {
              int stoppedIndex = resultRecord ["reason"].Count - 1;

              MiResultValue stoppedReason = resultRecord ["reason"] [stoppedIndex];

              switch (stoppedReason.GetString ())
              {
                case "exited":
                case "exited-normally":
                case "exited-signalled":
                {
                  if (m_interruptOperationCompleted != null)
                  {
                    m_interruptOperationCompleted.Set ();
                  }

                  ThreadPool.QueueUserWorkItem (delegate (object state)
                  {
                    try
                    {
                      LoggingUtils.RequireOk (Engine.Detach (NativeProgram.DebugProgram));
                    }
                    catch (Exception e)
                    {
                      LoggingUtils.HandleException (e);
                    }
                  });

                  break;
                }

                default:
                {
                  throw new NotImplementedException ();
                }
              }
            }
          }
          catch (Exception e)
          {
            LoggingUtils.HandleException (e);
          }

          break;
        }

        case "connected":
        {
          // 
          // ^connected: GDB has connected to a remote target.
          // 

          try
          {
            // 
            // If notifications are unsupported, we should assume that we need to refresh breakpoints when connected.
            //

            if (!GdbClient.GetClientFeatureSupported ("breakpoint-notifications"))
            {
              Engine.BreakpointManager.SetDirty (true);
            }

            Engine.BreakpointManager.RefreshBreakpoints ();
          }
          catch (Exception e)
          {
            LoggingUtils.HandleException (e);
          }

          break;
        }
        
        case "error":
        {
          // 
          // "^error" "," c-string: The operation failed. The c-string contains the corresponding error message.
          // 

          break;
        }
        
        case "exit":
        {
          // 
          // ^exit: GDB has terminated.
          // 

          break;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void OnClientStreamRecord (MiStreamRecord streamRecord)
    {
      LoggingUtils.PrintFunction ();

      switch (streamRecord.Type)
      {
        case MiStreamRecord.StreamType.Console:
        {
          // The console output stream contains text that should be displayed in the CLI console window. It contains the textual responses to CLI commands. 

          LoggingUtils.Print (string.Format ("[CLangDebugger] Console: {0}", streamRecord.Stream));

          break;
        }

        case MiStreamRecord.StreamType.Target:
        {
          // The console output stream contains text that should be displayed in the CLI console window. It contains the textual responses to CLI commands. 

          LoggingUtils.Print (string.Format ("[CLangDebugger] Target: {0}", streamRecord.Stream));

          break;
        }

        case MiStreamRecord.StreamType.Log:
        {
          // The log stream contains debugging messages being produced by gdb's internals.

          LoggingUtils.Print (string.Format ("[CLangDebugger] Log: {0}", streamRecord.Stream));

          if (streamRecord.Stream.Contains ("Remote communication error"))
          {
            ThreadPool.QueueUserWorkItem (delegate (object state)
            {
              try
              {
                LoggingUtils.RequireOk (Engine.Detach (NativeProgram.DebugProgram));
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }
            });
          }

          break;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void OnClientAsyncRecord (MiAsyncRecord asyncRecord)
    {
      LoggingUtils.PrintFunction ();

      switch (asyncRecord.Type)
      {
        case MiAsyncRecord.AsyncType.Exec: 
        {
          // 
          // Records prefixed '*'.
          // 

          switch (asyncRecord.Class)
          {
            case "running":
            {
              // 
              // The target is now running. The thread field tells which specific thread is now running, can be 'all' if every thread is running.
              // 

              lock (NativeProgram)
              {
                NativeProgram.SetRunning (true);

                string threadId = asyncRecord ["thread-id"] [0].GetString ();

                if (threadId.Equals ("all"))
                {
                  Dictionary<uint, DebuggeeThread> programThreads = NativeProgram.GetThreads ();

                  lock (programThreads)
                  {
                    foreach (DebuggeeThread thread in programThreads.Values)
                    {
                      thread.SetRunning (true);
                    }
                  }
                }
                else
                {
                  uint numericThreadId = uint.Parse (threadId);

                  NativeProgram.CurrentThreadId = numericThreadId;

                  CLangDebuggeeThread thread = NativeProgram.GetThread (numericThreadId);

                  if (thread != null)
                  {
                    thread.SetRunning (true);
                  }
                }
              }

              break;
            }

            case "stopped":
            {
              // 
              // The target has stopped.
              // 

              CLangDebuggeeThread stoppedThread = null;

              lock (NativeProgram)
              {
                NativeProgram.SetRunning (false);

                if (asyncRecord.HasField ("thread-id"))
                {
                  uint threadId = asyncRecord ["thread-id"] [0].GetUnsignedInt ();

                  NativeProgram.CurrentThreadId = threadId;
                }

                if (stoppedThread == null)
                {
                  stoppedThread = NativeProgram.GetThread (NativeProgram.CurrentThreadId);
                }

                if (stoppedThread != null)
                {
                  stoppedThread.SetRunning (false);
                }
                else
                {
                  throw new InvalidOperationException ("Could not evaluate a thread on which we stopped");
                }

                // 
                // Flag some or all of the program's threads as stopped, directed by 'stopped-threads' field.
                // 

                bool hasStoppedThreads = asyncRecord.HasField ("stopped-threads");

                if (hasStoppedThreads)
                {
                  // 
                  // If all threads are stopped, the stopped field will have the value of "all". 
                  // Otherwise, the value of the stopped field will be a list of thread identifiers.
                  // 

                  MiResultValue stoppedThreadsRecord = asyncRecord ["stopped-threads"] [0];

                  if (stoppedThreadsRecord is MiResultValueList)
                  {
                    MiResultValueList stoppedThreads = stoppedThreadsRecord as MiResultValueList;

                    foreach (MiResultValue stoppedThreadValue in stoppedThreads.List)
                    {
                      uint stoppedThreadId = stoppedThreadValue.GetUnsignedInt ();

                      CLangDebuggeeThread thread = NativeProgram.GetThread (stoppedThreadId);

                      if (thread != null)
                      {
                        thread.SetRunning (false);
                      }
                    }
                  }
                  else
                  {
                    Dictionary<uint, DebuggeeThread> programThreads = NativeProgram.GetThreads ();

                    lock (programThreads)
                    {
                      foreach (DebuggeeThread thread in programThreads.Values)
                      {
                        thread.SetRunning (false);
                      }
                    }
                  }
                }
              }

              // 
              // Unblocks waiting for 'stopped' to be processed. Skipping event handling during interrupt requests as it confuses VS debugger flow.
              // 

              bool ignoreInterruptSignal = false;

              if (m_interruptOperationCompleted != null)
              {
                m_interruptOperationCompleted.Set ();

                ignoreInterruptSignal = true;
              }

              // 
              // Process any pending requests to refresh registered breakpoints.
              // 

#if false
              RefreshSharedLibraries ();
#endif


#if false
              NativeProgram.RefreshAllThreads ();
#endif

              if (!GdbClient.GetClientFeatureSupported ("breakpoint-notifications"))
              {
                Engine.BreakpointManager.RefreshBreakpoints ();
              }

              // 
              // This behaviour seems at odds with the GDB/MI spec, but a *stopped event can contain
              // multiple 'reason' fields. This seems to occur mainly when signals have been ignored prior 
              // to a non-ignored triggering, i.e:
              // 
              //   Signal        Stop\tPrint\tPass to program\tDescription\n
              //   SIGSEGV       No\tYes\tYes\t\tSegmentation fault\n
              // 
              // *stopped,reason="signal-received",signal-name="SIGSEGV",signal-meaning="Segmentation fault",reason="signal-received",signal-name="SIGSEGV",signal-meaning="Segmentation fault",reason="exited-signalled",signal-name="SIGSEGV",signal-meaning="Segmentation fault"
              // 

              if (asyncRecord.HasField ("reason"))
              {
                // 
                // Here we pick the most recent (unhandled) signal.
                // 

                int stoppedIndex = asyncRecord ["reason"].Count - 1;

                MiResultValue stoppedReason = asyncRecord ["reason"] [stoppedIndex];

                // 
                // The reason field can have one of the following values:
                // 

                switch (stoppedReason.GetString ())
                {
                  case "breakpoint-hit":
                  case "watchpoint-trigger":
                  {
                    bool canContinue = true;

                    uint breakpointId = asyncRecord ["bkptno"] [0].GetUnsignedInt ();

                    string breakpointMode = asyncRecord ["disp"] [0].GetString ();

                    if (breakpointMode.Equals ("del"))
                    {
                      // 
                      // For temporary breakpoints, we won't have a valid managed object - so will just enforce a break event.
                      // 

                      //Engine.Broadcast (new DebugEngineEvent.Break (), NativeProgram.DebugProgram, stoppedThread);

                      Engine.Broadcast (new DebugEngineEvent.BreakpointHit (null), NativeProgram.DebugProgram, stoppedThread);
                    }
                    else
                    {
                      DebuggeeBreakpointBound boundBreakpoint = Engine.BreakpointManager.FindBoundBreakpoint (breakpointId);

                      if (boundBreakpoint == null)
                      {
                        // 
                        // Could not find the breakpoint we're looking for. Refresh everything and try again.
                        // 

                        Engine.BreakpointManager.SetDirty (true);

                        Engine.BreakpointManager.RefreshBreakpoints ();

                        boundBreakpoint = Engine.BreakpointManager.FindBoundBreakpoint (breakpointId);
                      }

                      if (boundBreakpoint == null)
                      {
                        // 
                        // Could not locate a registered breakpoint with matching id.
                        // 

                        DebugEngineEvent.Exception exception = new DebugEngineEvent.Exception (NativeProgram.DebugProgram, stoppedReason.GetString (), "Breakpoint #" + breakpointId + "hit", 0x00000000, canContinue);

                        Engine.Broadcast (exception, NativeProgram.DebugProgram, stoppedThread);
                      }
                      else
                      {
                        enum_BP_STATE [] breakpointState = new enum_BP_STATE [1];

                        LoggingUtils.RequireOk (boundBreakpoint.GetState (breakpointState));

                        if (breakpointState [0] == enum_BP_STATE.BPS_DELETED)
                        {
                          // 
                          // Hit a breakpoint which internally is flagged as deleted. Oh noes!
                          // 

                          DebugEngineEvent.Exception exception = new DebugEngineEvent.Exception (NativeProgram.DebugProgram, stoppedReason.GetString (), "Breakpoint #" + breakpointId + " hit [deleted]", 0x00000000, canContinue);

                          Engine.Broadcast (exception, NativeProgram.DebugProgram, stoppedThread);
                        }
                        else
                        {
                          // 
                          // Hit a breakpoint which is known about. Issue break event.
                          // 

                          IDebugBoundBreakpoint2 [] boundBreakpoints = new IDebugBoundBreakpoint2 [] { boundBreakpoint };

                          IEnumDebugBoundBreakpoints2 enumeratedBoundBreakpoint = new DebuggeeBreakpointBound.Enumerator (boundBreakpoints);

                          Engine.Broadcast (new DebugEngineEvent.BreakpointHit (enumeratedBoundBreakpoint), NativeProgram.DebugProgram, stoppedThread);
                        }
                      }
                    }

                    break;
                  }

                  case "end-stepping-range":
                  case "function-finished":
                  {
                    Engine.Broadcast (new DebugEngineEvent.StepComplete (), NativeProgram.DebugProgram, stoppedThread);

                    break;
                  }

                  case "signal-received":
                  {
                    string signalName = asyncRecord ["signal-name"] [stoppedIndex].GetString ();

                    string signalMeaning = asyncRecord ["signal-meaning"] [stoppedIndex].GetString ();

                    switch (signalName)
                    {
                      case null:
                      case "SIGINT":
                      {
                        if (!ignoreInterruptSignal)
                        {
                          Engine.Broadcast (new DebugEngineEvent.Break (), NativeProgram.DebugProgram, stoppedThread);
                        }

                        break;
                      }

                      default:
                      {
                        StringBuilder signalDescription = new StringBuilder ();

                        signalDescription.AppendFormat ("{0} ({1})", signalName, signalMeaning);

                        if (asyncRecord.HasField ("frame"))
                        {
                          MiResultValueTuple frameTuple = asyncRecord ["frame"] [0] as MiResultValueTuple;

                          if (frameTuple.HasField ("addr"))
                          {
                            string address = frameTuple ["addr"] [0].GetString ();

                            signalDescription.AppendFormat (" at {0}", address);
                          }

                          if (frameTuple.HasField ("func"))
                          {
                            string function = frameTuple ["func"] [0].GetString ();

                            signalDescription.AppendFormat (" ({0})", function);
                          }
                        }

                        bool canContinue = true;

                        DebugEngineEvent.Exception exception = new DebugEngineEvent.Exception (NativeProgram.DebugProgram, signalName, signalDescription.ToString (), 0x80000000, canContinue);

                        Engine.Broadcast (exception, NativeProgram.DebugProgram, stoppedThread);

                        break;
                      }
                    }

                    break;
                  }

                  case "read-watchpoint-trigger":
                  case "access-watchpoint-trigger":
                  case "location-reached":
                  case "watchpoint-scope":
                  case "solib-event":
                  case "fork":
                  case "vfork":
                  case "syscall-entry":
                  case "exec":
                  {
                    Engine.Broadcast (new DebugEngineEvent.Break (), NativeProgram.DebugProgram, stoppedThread);

                    break;
                  }

                  case "exited":
                  case "exited-normally":
                  case "exited-signalled":
                  {
                    // 
                    // React to program termination, but defer this so it doesn't consume the async output thread.
                    // 

                    ThreadPool.QueueUserWorkItem (delegate (object state)
                    {
                      try
                      {
                        LoggingUtils.RequireOk (Engine.Detach (NativeProgram.DebugProgram));
                      }
                      catch (Exception e)
                      {
                        LoggingUtils.HandleException (e);
                      }
                    });

                    break;
                  }
                }
              }

              break;
            }
          }

          break;
        }

        case MiAsyncRecord.AsyncType.Status:
        {
          // 
          // Records prefixed '+'.
          // 

          break;
        }


        case MiAsyncRecord.AsyncType.Notify:
        {
          // 
          // Records prefixed '='.
          // 

          switch (asyncRecord.Class)
          {
            case "thread-group-added":
            case "thread-group-started":
            {
              // 
              // A thread group became associated with a running program, either because the program was just started or the thread group was attached to a program.
              // 

              try
              {
                string threadGroupId = asyncRecord ["id"] [0].GetString ();

                m_threadGroupStatus [threadGroupId] = 0;
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }

              break;
            }

            case "thread-group-removed":
            case "thread-group-exited":
            {
              // 
              // A thread group is no longer associated with a running program, either because the program has exited, or because it was detached from.
              // 

              try
              {
                string threadGroupId = asyncRecord ["id"] [0].GetString ();

                if (asyncRecord.HasField ("exit-code"))
                {
                  m_threadGroupStatus [threadGroupId] = asyncRecord ["exit-code"] [0].GetUnsignedInt ();
                }
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }

              break;
            }

            case "thread-created":
            {
              // 
              // A thread either was created. The id field contains the gdb identifier of the thread. The gid field identifies the thread group this thread belongs to. 
              // 

              try
              {
                uint threadId = asyncRecord ["id"] [0].GetUnsignedInt ();

                string threadGroupId = asyncRecord ["group-id"] [0].GetString ();

                CLangDebuggeeThread thread = NativeProgram.GetThread (threadId);

                if (thread == null)
                {
                  NativeProgram.AddThread (threadId);
                }
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }

              break;
            }

            case "thread-exited":
            {
              // 
              // A thread has exited. The 'id' field contains the GDB identifier of the thread. The 'group-id' field identifies the thread group this thread belongs to. 
              // 

              try
              {
                uint threadId = asyncRecord ["id"] [0].GetUnsignedInt ();

                string threadGroupId = asyncRecord ["group-id"] [0].GetString ();

                uint exitCode = m_threadGroupStatus [threadGroupId];

                NativeProgram.RemoveThread (threadId, exitCode);
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }

              break;
            }

            case "thread-selected":
            {
              // 
              // Informs that the selected thread was changed as result of the last command.
              // 

              try
              {
                uint threadId = asyncRecord ["id"] [0].GetUnsignedInt ();

                NativeProgram.CurrentThreadId = threadId;
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }

              break;
            }

            case "library-loaded":
            {
              // 
              // Reports that a new library file was loaded by the program.
              // 

              try
              {
                string moduleName = asyncRecord ["id"] [0].GetString ();

                CLangDebuggeeModule module = NativeProgram.GetModule (moduleName);

                if (module == null)
                {
                  module = NativeProgram.AddModule (moduleName, asyncRecord);
                }

                if (!GdbClient.GetClientFeatureSupported ("breakpoint-notifications"))
                {
                  Engine.BreakpointManager.SetDirty (true);
                }
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }

              break;
            }

            case "library-unloaded":
            {
              // 
              // Reports that a library was unloaded by the program.
              // 

              try
              {
                string moduleName = asyncRecord ["id"] [0].GetString ();

                NativeProgram.RemoveModule (moduleName);

                if (!GdbClient.GetClientFeatureSupported ("breakpoint-notifications"))
                {
                  Engine.BreakpointManager.SetDirty (true);
                }
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }

              break;
            }

            case "breakpoint-created":
            case "breakpoint-modified":
            case "breakpoint-deleted":
            {
              try
              {
                IDebugPendingBreakpoint2 pendingBreakpoint = null;

                if (asyncRecord.HasField ("bkpt"))
                {
                  MiResultValue breakpointData = asyncRecord ["bkpt"] [0];

                  MiBreakpoint currentGdbBreakpoint = new MiBreakpoint (breakpointData.Values);

                  pendingBreakpoint = Engine.BreakpointManager.FindPendingBreakpoint (currentGdbBreakpoint.ID);

                  // If the breakpoint is unknown, this usually means it was bound externally to the IDE.
                  /*if (pendingBreakpoint == null)
                  {
                    // 
                    // CreatePendingBreakpoint always sets the dirty flag, so we need to reset this if it's handled immediately.
                    // 

                    DebugBreakpointRequest breakpointRequest = new DebugBreakpointRequest (currentGdbBreakpoint.Address);

                    LoggingUtils.RequireOk (Engine.BreakpointManager.CreatePendingBreakpoint (breakpointRequest, out pendingBreakpoint));
                  }*/
                }
                else if (asyncRecord.HasField ("id"))
                {
                  pendingBreakpoint = Engine.BreakpointManager.FindPendingBreakpoint (asyncRecord ["id"] [0].GetUnsignedInt ());
                }

                bool wasDirty = Engine.BreakpointManager.IsDirty ();

                if (pendingBreakpoint != null)
                {
                  DebuggeeBreakpointPending thisBreakpoint = pendingBreakpoint as DebuggeeBreakpointPending;

                  thisBreakpoint.RefreshBoundBreakpoints ();

                  thisBreakpoint.RefreshErrorBreakpoints ();
                }

                if (wasDirty)
                {
                  Engine.BreakpointManager.SetDirty (true);
                }
              }
              catch (Exception e)
              {
                LoggingUtils.HandleException (e);
              }

              break;
            }
          }

          break;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void RefreshSharedLibraries ()
    {
      // 
      // Retrieve a list of actively mapped shared libraries.
      // - This also triggers GDB to tell us about libraries which it may have missed.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        string command = string.Format ("-interpreter-exec console \"info sharedlibrary\"");

        GdbClient.SendCommand (command, delegate (MiResultRecord resultRecord)
        {
          MiResultRecord.RequireOk (resultRecord, command);

          string pattern = "(?<from>0x[0-9a-fA-F]+)[ ]+(?<to>0x[0-9a-fA-F]+)[ ]+(?<syms>Yes|No)[ ]+(?<lib>[^ $]+)";

          Regex regExMatcher = new Regex (pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

          for (int i = 0; i < resultRecord.Records.Count; ++i)
          {
            MiStreamRecord record = resultRecord.Records [i];

            if (!record.Stream.StartsWith ("0x"))
            {
              continue; // early rejection.
            }

            string unescapedStream = Regex.Unescape (record.Stream);

            Match regExLineMatch = regExMatcher.Match (unescapedStream);

            if (regExLineMatch.Success)
            {
              ulong from = ulong.Parse (regExLineMatch.Result ("${from}").Substring (2), NumberStyles.HexNumber);

              ulong to = ulong.Parse (regExLineMatch.Result ("${to}").Substring (2), NumberStyles.HexNumber);

              bool syms = regExLineMatch.Result ("${syms}").Equals ("Yes");

              string lib = regExLineMatch.Result ("${lib}").Replace ("\r", "").Replace ("\n", "");

              m_mappedSharedLibraries [lib] = new Tuple<ulong, ulong, bool> (from, to, syms);
            }
          }
        });
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }
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
