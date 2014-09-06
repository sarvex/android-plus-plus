﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.VisualStudio.Debugger.Interop;
using AndroidPlusPlus.Common;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.VsDebugEngine
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public class CLangDebuggeeThread : DebuggeeThread
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeThread (CLangDebuggeeProgram program, uint id)
      : base (program.DebugProgram, id, string.Empty)
    {
      NativeProgram = program;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeProgram NativeProgram { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public override void Refresh ()
    {
      m_debugProgram.AttachedEngine.NativeDebugger.RunInterruptOperation (delegate ()
      {
        string command = string.Format ("-thread-info {0}", m_threadId);

        MiResultRecord resultRecord = m_debugProgram.AttachedEngine.NativeDebugger.GdbClient.SendCommand (command);

        MiResultRecord.RequireOk (resultRecord, command);

        if (!resultRecord.HasField ("threads"))
        {
          throw new InvalidOperationException ("-thread-info result missing 'threads' field");
        }

        List<MiResultValue> threadDataList = resultRecord ["threads"];

        for (int i = 0; i < threadDataList.Count; ++i)
        {
          if (threadDataList [i].Values.Count > 0)
          {
            MiResultValueTuple threadData = (MiResultValueTuple) threadDataList [i] [0];

            uint threadId = threadData ["id"] [0].GetUnsignedInt ();

            if (threadId == m_threadId)
            {
              if (threadData.HasField ("name"))
              {
                m_threadName = threadData ["name"] [0].GetString (); // user-specified name
              }
              else if (threadData.HasField ("target-id"))
              {
                m_threadName = threadData ["target-id"] [0].GetString (); // usually the raw name, i.e. 'Thread 18771'
              }

              break;
            }
          }
        }
      });
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public override List<DebuggeeStackFrame> StackTrace (uint depth)
    {
      // 
      // Each thread maintains an internal cache of the last reported stack-trace. This is only cleared when threads are resumed via 'SetRunning(true)'.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        if (m_threadStackFrames.Count < depth)
        {
          uint threadId;

          LoggingUtils.RequireOk (GetThreadId (out threadId));

          m_debugProgram.AttachedEngine.NativeDebugger.RunInterruptOperation (delegate ()
          {
            // 
            // Determine the maximum available stack depth.
            // 

            string command;

            MiResultRecord resultRecord;

            if (depth == uint.MaxValue)
            {
              command = string.Format ("-stack-info-depth --thread {0}", threadId);

              resultRecord = m_debugProgram.AttachedEngine.NativeDebugger.GdbClient.SendCommand (command);

              MiResultRecord.RequireOk (resultRecord, command);

              depth = resultRecord ["depth"] [0].GetUnsignedInt ();
            }

            // 
            // Acquire stack frame information for any levels which we're missing.
            // 

            if (m_threadStackFrames.Count < depth)
            {
              command = string.Format ("-stack-list-frames --thread {0} {1} {2}", threadId, m_threadStackFrames.Count, depth - 1);

              resultRecord = m_debugProgram.AttachedEngine.NativeDebugger.GdbClient.SendCommand (command);

              MiResultRecord.RequireOk (resultRecord, command);

              if (resultRecord.HasField ("stack"))
              {
                MiResultValueList stackRecord = resultRecord ["stack"] [0] as MiResultValueList;

                for (int i = 0; i < stackRecord.Values.Count; ++i)
                {
                  MiResultValueTuple frameTuple = stackRecord [i] as MiResultValueTuple;

                  uint stackLevel = frameTuple ["level"] [0].GetUnsignedInt ();

                  string stackFrameId = m_threadName + "#" + stackLevel;

                  CLangDebuggeeStackFrame stackFrame = new CLangDebuggeeStackFrame (m_debugProgram.AttachedEngine.NativeDebugger, this, frameTuple, stackFrameId);

                  lock (m_threadStackFrames)
                  {
                    m_threadStackFrames.Add (stackFrame);
                  }
                }
              }
            }
          });
        }

        return m_threadStackFrames;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        throw;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public override int SetNextStatement (IDebugStackFrame2 stackFrame, IDebugCodeContext2 codeContext)
    {
      // 
      // Sets the next statement to the given stack frame and code context.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        CONTEXT_INFO [] contextInfo = new CONTEXT_INFO [1];

        LoggingUtils.RequireOk (codeContext.GetInfo (enum_CONTEXT_INFO_FIELDS.CIF_ADDRESSABSOLUTE, contextInfo));

        string location = "*" + contextInfo [0].bstrAddressAbsolute;

        m_debugProgram.AttachedEngine.NativeDebugger.RunInterruptOperation (delegate ()
        {
          // 
          // Create a temporary breakpoint to stop -exec-jump continuing when we'd rather it didn't.
          // 

          string command = string.Format ("-break-insert -t {0}", location);

          MiResultRecord resultRecord = m_debugProgram.AttachedEngine.NativeDebugger.GdbClient.SendCommand (command);

          MiResultRecord.RequireOk (resultRecord, command);

          // 
          // Jump to the specified address location.
          // 

          command = string.Format ("-exec-jump --thread {0} {1}", m_threadId, location);

          resultRecord = m_debugProgram.AttachedEngine.NativeDebugger.GdbClient.SendCommand (command);

          MiResultRecord.RequireOk (resultRecord, command);
        });

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
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
