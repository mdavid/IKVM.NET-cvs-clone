/*
  Copyright (C) 2009 Jeroen Frijters

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.

  Jeroen Frijters
  jeroen@frijters.net 

*/

package sun.awt;

import java.awt.AWTEvent;
import java.awt.GraphicsEnvironment;

public class SunToolkit
{
    public static AppContext targetToAppContext(Object target)
    {
	if (target == null || GraphicsEnvironment.isHeadless())
        {
	    return null;
        }
	// we only support a single AppContext
	return AppContext.getAppContext();
    }
    
    public static String getDataTransfererClassName()
    {
        throw new Error("Not implemented");
    }
    
    public static void executeOnEventHandlerThread(Object target, Runnable runnable)
    {
        throw new Error("Not implemented");
    }
    
    public static void postEvent(AppContext appContext, AWTEvent event)
    {
        throw new Error("Not implemented");
    }
}