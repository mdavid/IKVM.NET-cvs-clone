﻿/*
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
using System;

namespace IKVM.Reflection
{
	public abstract class MethodBase : MemberInfo
	{
		internal static readonly MethodBase[] EmptyArray = new MethodBase[0];

		internal abstract MethodSignature MethodSignature { get; }
		internal abstract int ParameterCount { get; }
		public abstract ParameterInfo[] GetParameters();
		public abstract MethodAttributes Attributes { get; }
		public abstract MethodImplAttributes GetMethodImplementationFlags();
		public abstract MethodBody GetMethodBody();
		public abstract CallingConventions CallingConvention { get; }

		public bool IsConstructor
		{
			get
			{
				if ((this.Attributes & MethodAttributes.RTSpecialName) != 0)
				{
					string name = this.Name;
					return name == ConstructorInfo.ConstructorName || name == ConstructorInfo.TypeConstructorName;
				}
				return false;
			}
		}

		public bool IsStatic
		{
			get { return (Attributes & MethodAttributes.Static) != 0; }
		}

		public bool IsVirtual
		{
			get { return (Attributes & MethodAttributes.Virtual) != 0; }
		}

		public bool IsAbstract
		{
			get { return (Attributes & MethodAttributes.Abstract) != 0; }
		}

		public bool IsFinal
		{
			get { return (Attributes & MethodAttributes.Final) != 0; }
		}

		public bool IsPublic
		{
			get { return (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public; }
		}

		public bool IsFamily
		{
			get { return (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family; }
		}

		public bool IsFamilyOrAssembly
		{
			get { return (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamORAssem; }
		}

		public bool IsAssembly
		{
			get { return (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Assembly; }
		}

		public bool IsFamilyAndAssembly
		{
			get { return (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamANDAssem; }
		}

		public bool IsPrivate
		{
			get { return (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private; }
		}

		public bool IsSpecialName
		{
			get { return (Attributes & MethodAttributes.SpecialName) != 0; }
		}

		public bool IsHideBySig
		{
			get { return (Attributes & MethodAttributes.HideBySig) != 0; }
		}

		public virtual Type[] GetGenericArguments()
		{
			return Type.EmptyTypes;
		}

		public virtual bool IsGenericMethod
		{
			get { return false; }
		}

		public virtual bool IsGenericMethodDefinition
		{
			get { return false; }
		}

		public virtual bool ContainsGenericParameters
		{
			get { return IsGenericMethodDefinition; }
		}

		// This goes to the (uninstantiated) MethodInfo on the (uninstantiated) Type. For constructors
		// it also has the effect of removing the ConstructorInfo wrapper and returning the underlying MethodInfo.
		internal abstract MethodInfo GetMethodOnTypeDefinition();

		internal static void WrapGenericParameterInfos(MemberInfo member, IGenericBinder binder, ParameterInfo[] parameterInfo)
		{
			for (int i = 0; i < parameterInfo.Length; i++)
			{
				parameterInfo[i] = new GenericParameterInfoImpl(member, binder, parameterInfo[i]);
			}
		}

		internal abstract int ImportTo(Emit.ModuleBuilder module);
	}
}