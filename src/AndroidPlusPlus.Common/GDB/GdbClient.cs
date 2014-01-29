﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.Common
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public sealed class GdbClient : AsyncRedirectProcess.EventListener, IDisposable
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public enum StepType
    {
      Statement,
      Line,
      Instruction
    };

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public delegate void OnResultRecordDelegate (MiResultRecord resultRecord);

    public delegate void OnAsyncRecordDelegate (MiAsyncRecord asyncRecord);

    public delegate void OnStreamRecordDelegate (MiStreamRecord streamRecord);

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public OnResultRecordDelegate OnResultRecord { get; set; }

    public OnAsyncRecordDelegate OnAsyncRecord { get; set; }

    public OnStreamRecordDelegate OnStreamRecord { get; set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private class AsyncCommandData
    {
      public AsyncCommandData ()
      {
        StreamRecords = new List<MiStreamRecord> ();
      }

      public string Command { get; set; }

      public List<MiStreamRecord> StreamRecords;

      public OnResultRecordDelegate ResultDelegate { get; set; }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private readonly GdbSetup m_gdbSetup;

    private GdbServer m_gdbServer;

    private AsyncRedirectProcess m_gdbClientInstance;

    private Dictionary<uint, AsyncCommandData> m_asyncCommandData;

    private ManualResetEvent m_syncCommandLock;

    private int m_sessionLastActivityTick;

    private uint m_sessionCommandToken;

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public GdbClient (GdbSetup gdbSetup)
    {
      m_gdbSetup = gdbSetup;

      m_gdbServer = null;

      m_gdbClientInstance = null;

      m_asyncCommandData = new Dictionary<uint, AsyncCommandData> ();

      m_syncCommandLock = null;

      m_sessionLastActivityTick = Environment.TickCount;

      m_sessionCommandToken = 1; // Start at 1 so 0 can represent an invalid token.
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose ()
    {
      Trace.WriteLine (string.Format ("[GdbClient] Dispose:"));

      SendAsyncCommand ("-gdb-exit");

      if (m_gdbClientInstance != null)
      {
        m_gdbClientInstance.Dispose ();

        m_gdbClientInstance = null;
      }

      if (m_syncCommandLock != null)
      {
        m_syncCommandLock.Dispose ();

        m_syncCommandLock = null;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Start ()
    {
      Trace.WriteLine (string.Format ("[GdbClient] Start:"));

      m_gdbClientInstance = new AsyncRedirectProcess (m_gdbSetup.GdbToolPath, m_gdbSetup.GdbToolArguments);

      m_gdbClientInstance.Listener = this;

      if (File.Exists (m_gdbSetup.CacheDirectory + @"\gdb.setup"))
      {
        m_gdbClientInstance.StartInfo.Arguments += string.Format (@" -x {0}\gdb.setup", StringUtils.ConvertPathWindowsToPosix (m_gdbSetup.CacheDirectory));
      }

      m_gdbClientInstance.Start ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Attach (GdbServer gdbServer)
    {
      Trace.WriteLine (string.Format ("[GdbClient] Attach:"));

      m_gdbServer = gdbServer;

      m_gdbSetup.SetupPortForwarding ();

      SetSetting ("solib-search-path", m_gdbSetup.CacheDirectory);

      string [] cachedBinaries = m_gdbSetup.CacheDeviceBinaries ();

      foreach (string binary in cachedBinaries)
      {
        SendCommand ("symbol-file " + StringUtils.ConvertPathWindowsToPosix (binary));
      }

      string [] execCommands = m_gdbSetup.CreateGdbExecutionScript ();

      foreach (string command in execCommands)
      {
        SendCommand (command);
      }

      SendCommand (string.Format ("target remote {0}:{1}", m_gdbSetup.Host, m_gdbSetup.Port), 60000);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Detach ()
    {
      Trace.WriteLine (string.Format ("[GdbClient] Detach:"));

      SendAsyncCommand ("-target-detach");

      m_gdbServer = null;

      m_gdbSetup.ClearPortForwarding ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Stop ()
    {
      Trace.WriteLine (string.Format ("[GdbClient] Stop:"));

      SendCommand ("-exec-interrupt");
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Continue ()
    {
      Trace.WriteLine (string.Format ("[GdbClient] Continue:"));

      SendCommand ("-exec-continue");
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void Terminate ()
    {
      Trace.WriteLine (string.Format ("[GdbClient] Terminate:"));

      SendAsyncCommand ("kill");
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void StepInto (StepType stepType, bool reverse)
    {
      switch (stepType)
      {
        case StepType.Statement:
        case StepType.Line:
        {
          MiResultRecord resultRecord = SendCommand (string.Format ("-exec-step {0}", ((reverse) ? "--reverse" : "")));

          if ((resultRecord == null) || ((resultRecord != null) && resultRecord.IsError ()))
          {
            throw new InvalidOperationException ();
          }

          break;
        }
        case StepType.Instruction:
        {
          MiResultRecord resultRecord = SendCommand (string.Format ("-exec-step-instruction {0}", ((reverse) ? "--reverse" : "")));

          if ((resultRecord == null) || ((resultRecord != null) && resultRecord.IsError ()))
          {
            throw new InvalidOperationException ();
          }

          break;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void StepOut (StepType stepType, bool reverse)
    {
      switch (stepType)
      {
        case StepType.Statement:
        case StepType.Line:
        case StepType.Instruction:
        {
          MiResultRecord resultRecord = SendCommand (string.Format ("-exec-finish {0}", ((reverse) ? "--reverse" : "")));

          if ((resultRecord == null) || ((resultRecord != null) && resultRecord.IsError ()))
          {
            throw new InvalidOperationException ();
          }

          break;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void StepOver (StepType stepType, bool reverse)
    {
      switch (stepType)
      {
        case StepType.Statement:
        case StepType.Line:
        {
          MiResultRecord resultRecord = SendCommand (string.Format ("-exec-next {0}", ((reverse) ? "--reverse" : "")));

          if ((resultRecord == null) || ((resultRecord != null) && resultRecord.IsError ()))
          {
            throw new InvalidOperationException ();
          }

          break;
        }
        case StepType.Instruction:
        {
          MiResultRecord resultRecord = SendCommand (string.Format ("-exec-next-instruction {0}", ((reverse) ? "--reverse" : "")));

          if ((resultRecord == null) || ((resultRecord != null) && resultRecord.IsError ()))
          {
            throw new InvalidOperationException ();
          }

          break;
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public string GetSetting (string setting)
    {
      Trace.WriteLine (string.Format ("[GdbClient] GetSetting: " + setting));

      MiResultRecord result = SendCommand (string.Format ("-gdb-show {0}", setting));

      if ((result != null) && (!result.IsError ()))
      {
        return result ["value"].GetString ();
      }

      return string.Empty;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void SetSetting (string setting, string value)
    {
      Trace.WriteLine (string.Format ("[GdbClient] SetSetting: " + setting + ", " + value));

      SendCommand (string.Format ("-gdb-set {0} {1}", setting, value));
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public MiResultRecord SendCommand (string command, int timeout = 60000)
    {
      // 
      // Perform a synchronous command request; issue a standard async command and keep alive whilst still receiving output.
      // 

      Trace.WriteLine (string.Format ("[GdbClient] SendCommand: {0}", command));

      MiResultRecord syncResultRecord = null;

      if (m_gdbClientInstance == null)
      {
        return syncResultRecord;
      }

      lock (this)
      {
        m_syncCommandLock = new ManualResetEvent (false);

        SendAsyncCommand (command, delegate (MiResultRecord record) 
        {
          syncResultRecord = record;

          if (m_syncCommandLock != null)
          {
            m_syncCommandLock.Set ();
          }
        });

        Thread.Yield ();

        // 
        // Wait for asynchronous record response (or exit), reset timeout each time new activity occurs.
        // 

        int timeoutFromCurrentTick = timeout;

        bool responseSignaled = false;

        while ((!responseSignaled) && (timeoutFromCurrentTick > 0))
        {
          responseSignaled = m_syncCommandLock.WaitOne (timeoutFromCurrentTick);

          timeoutFromCurrentTick = (timeout + m_sessionLastActivityTick) - Environment.TickCount;

          Thread.Yield ();
        }

        if (!responseSignaled)
        {
          throw new TimeoutException ("Timed out waiting for synchronous SendCommand response.");
        }

        m_syncCommandLock = null;
      }

      return syncResultRecord;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void SendAsyncCommand (string command, OnResultRecordDelegate asyncDelegate = null)
    {
      // 
      // Keep track of this command, and associated token-id, so results can be tracked asynchronously.
      // 

      Trace.WriteLine (string.Format ("[GdbClient] SendAsyncCommand: {0}", command));

      if (m_gdbClientInstance == null)
      {
        return;
      }

      lock (this)
      {
        AsyncCommandData commandData = new AsyncCommandData ();

        commandData.Command = command;

        commandData.ResultDelegate = asyncDelegate;

        m_asyncCommandData.Add (m_sessionCommandToken, commandData);

        // 
        // Prepend (and increment) GDB/MI token.
        // 

        command = m_sessionCommandToken + command;

        ++m_sessionCommandToken;

        m_gdbClientInstance.SendCommand (command);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void ProcessStdout (object sendingProcess, DataReceivedEventArgs args)
    {
      if (!string.IsNullOrEmpty (args.Data))
      {
        m_sessionLastActivityTick = Environment.TickCount;

        Trace.WriteLine (string.Format ("[GdbClient] ProcessStdout: {0}", args.Data));

        // 
        // Distribute result records to registered delegate callbacks.
        // 

        MiRecord record = MiInterpreter.ParseGdbOutputRecord (args.Data);

        if (record is MiPromptRecord)
        {
          //m_asyncCommandLock.Set ();
        }
        else if ((record is MiAsyncRecord) && (OnAsyncRecord != null))
        {
          MiAsyncRecord asyncRecord = record as MiAsyncRecord;

          OnAsyncRecord (asyncRecord);
        }
        else if ((record is MiResultRecord) && (OnResultRecord != null))
        {
          MiResultRecord resultRecord = record as MiResultRecord;

          OnResultRecord (resultRecord);
        }
        else if ((record is MiStreamRecord) && (OnStreamRecord != null))
        {
          MiStreamRecord streamRecord = record as MiStreamRecord;

          OnStreamRecord (streamRecord);

          // 
          // Non-GDB/MI commands (standard client interface commands) report their output using standard stream records.
          // We cache these outputs for any active CLI commands, identifiable as the commands don't start with '-'.
          // 

          lock (m_asyncCommandData)
          {
            foreach (KeyValuePair<uint, AsyncCommandData> asyncCommand in m_asyncCommandData)
            {
              if (!asyncCommand.Value.Command.StartsWith ("-"))
              {
                asyncCommand.Value.StreamRecords.Add (streamRecord);
              }
            }
          }
        }

        // 
        // Call the corresponding registered delegate for the token response.
        // 

        MiResultRecord callbackRecord = record as MiResultRecord;

        if ((callbackRecord != null) && (callbackRecord.Token != 0))
        {
          AsyncCommandData callbackCommandData = null;

          lock (m_asyncCommandData)
          {
            if (m_asyncCommandData.TryGetValue (callbackRecord.Token, out callbackCommandData))
            {
              callbackRecord.Records.AddRange (callbackCommandData.StreamRecords);

              if (callbackCommandData.ResultDelegate != null)
              {
                callbackCommandData.ResultDelegate (callbackRecord);
              }

              m_asyncCommandData.Remove (callbackRecord.Token);
            }
          }
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void ProcessStderr (object sendingProcess, DataReceivedEventArgs args)
    {
      if (!string.IsNullOrEmpty (args.Data))
      {
        m_sessionLastActivityTick = Environment.TickCount;

        Trace.WriteLine (string.Format ("[GdbClient] ProcessStderr: {0}", args.Data));
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void ProcessExited (object sendingProcess, EventArgs args)
    {
      m_sessionLastActivityTick = Environment.TickCount;

      Trace.WriteLine (string.Format ("[GdbClient] ProcessExited: {0}", args));

      m_gdbClientInstance.Dispose ();

      m_gdbClientInstance = null;

      // 
      // If we're waiting on a synchronous command, signal a finish to process termination.
      // 

      if (m_syncCommandLock != null)
      {
        m_syncCommandLock.Set ();
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
