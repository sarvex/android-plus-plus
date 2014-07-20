﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Threading;

using Microsoft.Build.Framework;
using Microsoft.Win32;
using Microsoft.Build.Utilities;

using AndroidPlusPlus.MsBuild.Common;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.MsBuild.CppTasks
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public class GccCompile : TrackedOutOfDateToolTask, ITask
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public GccCompile ()
      : base (new ResourceManager ("AndroidPlusPlus.MsBuild.CppTasks.Properties.Resources", Assembly.GetExecutingAssembly ()))
    {
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override void TrackedExecuteToolOutput (KeyValuePair<string, List<ITaskItem>> commandAndSourceFiles, string singleLine)
    {
      if (ToolExe.StartsWith ("clang"))
      {
        LogEventsFromTextOutput (singleLine, MessageImportance.High);
      }
      else
      {
        // 
        // GCC output differs from a Visual Studio's "jump to line" format, we transform that output here.
        // 

        LogEventsFromTextOutput (GccUtilities.ConvertGccOutputToVS (singleLine), MessageImportance.High);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override string GenerateCommandLineFromProps (ITaskItem source)
    {
      // 
      // Build a command-line based on parsing switches from the registered property sheet, and any additional flags.
      // 

      StringBuilder builder = new StringBuilder (PathUtils.CommandLineLength);

      try
      {
        if (source == null)
        {
          throw new ArgumentNullException ();
        }

        builder.Append (m_parsedProperties.Parse (source));

        builder.Append (" -c "); // compile the C/C++ file
      }
      catch (Exception e)
      {
        Log.LogErrorFromException (e, true);
      }

      return builder.ToString ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override void AddTaskSpecificDependencies (ref TrackedFileManager trackedFileManager, ITaskItem [] sources)
    {
      // 
      // Register additional 'forced include' usage for each of the sources.
      // 

      foreach (ITaskItem source in sources)
      {
        try
        {
          if (!string.IsNullOrWhiteSpace (source.GetMetadata ("ForcedIncludeFiles")))
          {
            string [] forcedIncludeFiles = source.GetMetadata ("ForcedIncludeFiles").Split (new char [] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            List<ITaskItem> forcedIncludeItems = new List<ITaskItem> ();

            foreach (string file in forcedIncludeFiles)
            {
              // 
              // Supports including pre-compiled headers via '-include' when they need to be referenced without '.pch'/'.gch'. Fix this.
              // 

              string fileFullPath = Path.GetFullPath (file);

              if ((ToolExe.StartsWith ("clang")) && (File.Exists (fileFullPath + ".pch")))
              {
                fileFullPath = fileFullPath + ".pch";
              }
              else if (File.Exists (fileFullPath + ".gch"))
              {
                fileFullPath = fileFullPath + ".gch";
              }

              // 
              // Also validate that we don't try adding dependencies to missing files, as this breaks tracking.
              // 

              if (!File.Exists (fileFullPath))
              {
                throw new FileNotFoundException ("Could not find 'forced include' dependency: " + fileFullPath);
              }

              forcedIncludeItems.Add (new TaskItem (fileFullPath));
            }

            trackedFileManager.AddDependencyForSources (forcedIncludeItems.ToArray (), new ITaskItem [] { source });
          }
        }
        catch (Exception e)
        {
          Log.LogWarningFromException (e, false);
        }
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected override string ToolName
    {
      get
      {
        return "GccCompile";
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
