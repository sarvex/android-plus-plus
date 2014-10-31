////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

package com.androidplusplus.remotehub;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import android.util.Log;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

// 
// BroadcastReceiver to handle a request for launching the GDBSERVER in a manner to workaround issues introduced in Android 4.3
// - The receiver is time-sensitive, so it simply spawns a service to perform the bulk of the required workload.
// 
// am broadcast 
//  -a com.androidplusplus.remotehub.intent.action.LAUNCH_GDBSERVER 
//  --include-stopped-packages
// 

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public class GdbServerWorkaroundReceiver extends BroadcastReceiver
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  private static final String TAG = "GdbServerWorkaroundReceiver";

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  @Override
  public void onReceive (Context context, Intent intent)
  {
    Log.v (TAG, "onReceive: " + intent);

    Intent launchWorkaroundServiceIntent = new Intent (context, GdbServerWorkaroundService.class);

    if (intent != null)
    {
      Bundle extras = intent.getExtras ();

      if (extras != null)
      {
        launchWorkaroundServiceIntent.putExtras (extras);
      }
    }

    context.startService (launchWorkaroundServiceIntent);
  }

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
