﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using AndroidPlusPlus.Common;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.VsDebugEngine
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public class CLangDebuggerEvent
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [Guid ("500544A2-5E12-44F8-9694-56F41FA13188")]
    public sealed class StartServer : SynchronousDebugEvent
    {
      public CLangDebugger Debugger { get; private set; }

      public StartServer (CLangDebugger debugger)
      {
        Debugger = debugger;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [Guid ("D0464B89-1DDD-4A62-B23D-F748079F2BE8")]
    public sealed class StopServer : SynchronousDebugEvent
    {
      public CLangDebugger Debugger { get; private set; }

      public StopServer (CLangDebugger debugger)
      {
        Debugger = debugger;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [Guid ("E940501E-CFA7-478D-A8FB-745B95C4FF91")]
    public sealed class AttachClient : SynchronousDebugEvent
    {
      public CLangDebugger Debugger { get; private set; }

      public AttachClient (CLangDebugger debugger)
      {
        Debugger = debugger;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [Guid ("4ABD7548-9660-4732-A43D-360C47DFAD4C")]
    public sealed class DetachClient : SynchronousDebugEvent
    {
      public CLangDebugger Debugger { get; private set; }

      public DetachClient (CLangDebugger debugger)
      {
        Debugger = debugger;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [Guid ("4DB8FE03-28F5-47D4-8787-B3CFC962FE8A")]
    public sealed class StopClient : SynchronousDebugEvent
    {
      public CLangDebugger Debugger { get; private set; }

      public StopClient (CLangDebugger debugger)
      {
        Debugger = debugger;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [Guid ("B91F7449-AA4D-464D-A768-BCDA07F9C7AD")]
    public sealed class ContinueClient : SynchronousDebugEvent
    {
      public CLangDebugger Debugger { get; private set; }

      public ContinueClient (CLangDebugger debugger)
      {
        Debugger = debugger;
      }
    }


    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    [Guid ("958CE25E-0FA7-4CA9-A238-8110E9D34358")]
    public sealed class TerminateClient : SynchronousDebugEvent
    {
      public CLangDebugger Debugger { get; private set; }

      public TerminateClient (CLangDebugger debugger)
      {
        Debugger = debugger;
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
