﻿/*
  Copyright (C) 2008-2010 Jeroen Frijters

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
using System.Collections.Generic;
#if STATIC_COMPILER
using IKVM.Reflection;
using IKVM.Reflection.Emit;
using Type = IKVM.Reflection.Type;
#else
using System.Reflection;
using System.Reflection.Emit;
#endif
using System.Diagnostics;
using Instruction = IKVM.Internal.ClassFile.Method.Instruction;
using InstructionFlags = IKVM.Internal.ClassFile.Method.InstructionFlags;

namespace IKVM.Internal
{
	static class Intrinsics
	{
		private delegate bool Emitter(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags);
		private struct IntrinsicKey : IEquatable<IntrinsicKey>
		{
			private readonly string className;
			private readonly string methodName;
			private readonly string methodSignature;

			internal IntrinsicKey(string className, string methodName, string methodSignature)
			{
				this.className = string.Intern(className);
				this.methodName = string.Intern(methodName);
				this.methodSignature = string.Intern(methodSignature);
			}

			internal IntrinsicKey(MethodWrapper mw)
			{
				this.className = mw.DeclaringType.Name;
				this.methodName = mw.Name;
				this.methodSignature = mw.Signature;
			}

			public override bool Equals(object obj)
			{
				return Equals((IntrinsicKey)obj);
			}

			public bool Equals(IntrinsicKey other)
			{
				return ReferenceEquals(className, other.className) && ReferenceEquals(methodName, other.methodName) && ReferenceEquals(methodSignature, other.methodSignature);
			}

			public override int GetHashCode()
			{
				return methodName.GetHashCode();
			}
		}
		private static readonly Dictionary<IntrinsicKey, Emitter> intrinsics = Register();
#if STATIC_COMPILER
		private static readonly Type typeofFloatConverter = StaticCompiler.GetRuntimeType("IKVM.Runtime.FloatConverter");
		private static readonly Type typeofDoubleConverter = StaticCompiler.GetRuntimeType("IKVM.Runtime.DoubleConverter");
#else
		private static readonly Type typeofFloatConverter = typeof(IKVM.Runtime.FloatConverter);
		private static readonly Type typeofDoubleConverter = typeof(IKVM.Runtime.DoubleConverter);
#endif

		private static Dictionary<IntrinsicKey, Emitter> Register()
		{
			Dictionary<IntrinsicKey, Emitter> intrinsics = new Dictionary<IntrinsicKey, Emitter>();
			intrinsics.Add(new IntrinsicKey("java.lang.Object", "getClass", "()Ljava.lang.Class;"), Object_getClass);
			intrinsics.Add(new IntrinsicKey("java.lang.Class", "desiredAssertionStatus", "()Z"), Class_desiredAssertionStatus);
			intrinsics.Add(new IntrinsicKey("java.lang.Float", "floatToRawIntBits", "(F)I"), Float_floatToRawIntBits);
			intrinsics.Add(new IntrinsicKey("java.lang.Float", "intBitsToFloat", "(I)F"), Float_intBitsToFloat);
			intrinsics.Add(new IntrinsicKey("java.lang.Double", "doubleToRawLongBits", "(D)J"), Double_doubleToRawLongBits);
			intrinsics.Add(new IntrinsicKey("java.lang.Double", "longBitsToDouble", "(J)D"), Double_longBitsToDouble);
			intrinsics.Add(new IntrinsicKey("java.lang.System", "arraycopy", "(Ljava.lang.Object;ILjava.lang.Object;II)V"), System_arraycopy);
			intrinsics.Add(new IntrinsicKey("java.util.concurrent.atomic.AtomicReferenceFieldUpdater", "newUpdater", "(Ljava.lang.Class;Ljava.lang.Class;Ljava.lang.String;)Ljava.util.concurrent.atomic.AtomicReferenceFieldUpdater;"), AtomicReferenceFieldUpdater_newUpdater);
#if STATIC_COMPILER
			// String_toCharArray relies on globals, which aren't usable in dynamic mode
			intrinsics.Add(new IntrinsicKey("java.lang.String", "toCharArray", "()[C"), String_toCharArray);
#endif
			intrinsics.Add(new IntrinsicKey("sun.reflect.Reflection", "getCallerClass", "(I)Ljava.lang.Class;"), Reflection_getCallerClass);
			intrinsics.Add(new IntrinsicKey("java.lang.ClassLoader", "getCallerClassLoader", "()Ljava.lang.ClassLoader;"), ClassLoader_getCallerClassLoader);
			intrinsics.Add(new IntrinsicKey("ikvm.internal.CallerID", "getCallerID", "()Likvm.internal.CallerID;"), CallerID_getCallerID);
			intrinsics.Add(new IntrinsicKey("ikvm.runtime.Util", "getInstanceTypeFromClass", "(Ljava.lang.Class;)Lcli.System.Type;"), Util_getInstanceTypeFromClass);
#if STATIC_COMPILER
			// this only applies to the core class library, so makes no sense in dynamic mode
			intrinsics.Add(new IntrinsicKey("java.lang.Class", "getPrimitiveClass", "(Ljava.lang.String;)Ljava.lang.Class;"), Class_getPrimitiveClass);
#endif
			intrinsics.Add(new IntrinsicKey("java.lang.ThreadLocal", "<init>", "()V"), ThreadLocal_new);
			return intrinsics;
		}

		internal static bool IsIntrinsic(MethodWrapper mw)
		{
			return intrinsics.ContainsKey(new IntrinsicKey(mw)) && mw.DeclaringType.GetClassLoader() == CoreClasses.java.lang.Object.Wrapper.GetClassLoader();
		}

		internal static bool Emit(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			// note that intrinsics can always refuse to emit code and the code generator will fall back to a normal method call
			return intrinsics[new IntrinsicKey(method)](context, ilgen, method, ma, opcodeIndex, caller, classFile, code, flags);
		}

		private static bool Object_getClass(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			// this is the null-check idiom that javac uses (both in its own source and in the code it generates)
			if (code[opcodeIndex + 1].NormalizedOpCode == NormalizedByteCode.__pop)
			{
				ilgen.Emit(OpCodes.Dup);
				ilgen.EmitNullCheck();
				return true;
			}
			// this optimizes obj1.getClass() ==/!= obj2.getClass()
			else if (opcodeIndex + 3 < code.Length
				&& (flags[opcodeIndex + 1] & InstructionFlags.BranchTarget) == 0
				&& (flags[opcodeIndex + 2] & InstructionFlags.BranchTarget) == 0
				&& (flags[opcodeIndex + 3] & InstructionFlags.BranchTarget) == 0
				&& code[opcodeIndex + 1].NormalizedOpCode == NormalizedByteCode.__aload
				&& code[opcodeIndex + 2].NormalizedOpCode == NormalizedByteCode.__invokevirtual
				&& (code[opcodeIndex + 3].NormalizedOpCode == NormalizedByteCode.__if_acmpeq || code[opcodeIndex + 3].NormalizedOpCode == NormalizedByteCode.__if_acmpne)
				&& (IsSafeForGetClassOptimization(ma.GetStackTypeWrapper(opcodeIndex, 0)) || IsSafeForGetClassOptimization(ma.GetStackTypeWrapper(opcodeIndex + 2, 0))))
			{
				ClassFile.ConstantPoolItemMI cpi = classFile.GetMethodref(code[opcodeIndex + 2].Arg1);
				if (cpi.Class == "java.lang.Object" && cpi.Name == "getClass" && cpi.Signature == "()Ljava.lang.Class;")
				{
					// we can't patch the current opcode, so we have to emit the first call to GetTypeHandle here
					ilgen.Emit(OpCodes.Callvirt, Compiler.getTypeMethod);
					code[opcodeIndex + 2].PatchOpCode(NormalizedByteCode.__intrinsic_gettype);
					return true;
				}
			}
			return false;
		}

		private static bool Class_desiredAssertionStatus(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			TypeWrapper classLiteral = ilgen.PeekLazyClassLiteral();
			if (classLiteral != null && classLiteral.GetClassLoader().RemoveAsserts)
			{
				ilgen.LazyEmitPop();
				ilgen.LazyEmitLdc_I4(0);
				return true;
			}
			else
			{
				return false;
			}
		}

		private static bool IsSafeForGetClassOptimization(TypeWrapper tw)
		{
			// because of ghost arrays, we don't optimize if both types are either java.lang.Object or an array
			return tw != CoreClasses.java.lang.Object.Wrapper && !tw.IsArray;
		}

		private static bool Float_floatToRawIntBits(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			EmitConversion(ilgen, typeofFloatConverter, "ToInt");
			return true;
		}

		private static bool Float_intBitsToFloat(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			EmitConversion(ilgen, typeofFloatConverter, "ToFloat");
			return true;
		}

		private static bool Double_doubleToRawLongBits(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			EmitConversion(ilgen, typeofDoubleConverter, "ToLong");
			return true;
		}

		private static bool Double_longBitsToDouble(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			EmitConversion(ilgen, typeofDoubleConverter, "ToDouble");
			return true;
		}

		private static void EmitConversion(CodeEmitter ilgen, Type converterType, string method)
		{
			LocalBuilder converter = ilgen.UnsafeAllocTempLocal(converterType);
			ilgen.Emit(OpCodes.Ldloca, converter);
			ilgen.Emit(OpCodes.Call, converterType.GetMethod(method));
		}

		private static bool System_arraycopy(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			// if the array arguments on the stack are of a known array type, we can redirect to an optimized version of arraycopy.
			// Note that we also have to handle VMSystem.arraycopy, because on GNU Classpath StringBuffer directly calls
			// this method to avoid prematurely initialising System.
			TypeWrapper dst_type = ma.GetStackTypeWrapper(opcodeIndex, 2);
			TypeWrapper src_type = ma.GetStackTypeWrapper(opcodeIndex, 4);
			if (!dst_type.IsUnloadable && dst_type.IsArray && dst_type == src_type)
			{
				switch (dst_type.Name[1])
				{
					case 'J':
					case 'D':
						ilgen.Emit(OpCodes.Call, ByteCodeHelperMethods.arraycopy_primitive_8);
						break;
					case 'I':
					case 'F':
						ilgen.Emit(OpCodes.Call, ByteCodeHelperMethods.arraycopy_primitive_4);
						break;
					case 'S':
					case 'C':
						ilgen.Emit(OpCodes.Call, ByteCodeHelperMethods.arraycopy_primitive_2);
						break;
					case 'B':
					case 'Z':
						ilgen.Emit(OpCodes.Call, ByteCodeHelperMethods.arraycopy_primitive_1);
						break;
					default:
						// TODO once the verifier tracks actual types (i.e. it knows that
						// a particular reference is the result of a "new" opcode) we can
						// use the fast version if the exact destination type is known
						// (in that case the "dst_type == src_type" above should
						// be changed to "src_type.IsAssignableTo(dst_type)".
						TypeWrapper elemtw = dst_type.ElementTypeWrapper;
						// note that IsFinal returns true for array types, so we have to be careful!
						if (!elemtw.IsArray && elemtw.IsFinal)
						{
							ilgen.Emit(OpCodes.Call, ByteCodeHelperMethods.arraycopy_fast);
						}
						else
						{
							ilgen.Emit(OpCodes.Call, ByteCodeHelperMethods.arraycopy);
						}
						break;
				}
				return true;
			}
			else
			{
				return false;
			}
		}

		private static bool AtomicReferenceFieldUpdater_newUpdater(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			return AtomicReferenceFieldUpdaterEmitter.Emit(context, caller.DeclaringType, ilgen, classFile, opcodeIndex, code, flags);
		}

		private static bool String_toCharArray(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			string str = ilgen.PopLazyLdstr();
			if (str != null)
			{
				// arbitrary length for "big" strings
				if (str.Length > 128)
				{
					EmitLoadCharArrayLiteral(ilgen, str, caller.DeclaringType);
					return true;
				}
				ilgen.Emit(OpCodes.Ldstr, str);
			}
			return false;
		}

		private static void EmitLoadCharArrayLiteral(CodeEmitter ilgen, string str, TypeWrapper tw)
		{
			ModuleBuilder mod = tw.GetClassLoader().GetTypeWrapperFactory().ModuleBuilder;
			// FXBUG on .NET 1.1 & 2.0 the value type that Ref.Emit automatically generates is public,
			// so we pre-create a non-public type with the right name here and it will "magically" use
			// that instead.
			// If we're running on Mono this isn't necessary, but for simplicitly we'll simply create
			// the type as well (it is useless, but all it does is waste a little space).
			int length = str.Length * 2;
			string typename = "$ArrayType$" + length;
			Type type = mod.GetType(typename, false, false);
			if (type == null)
			{
				if (tw.GetClassLoader().GetTypeWrapperFactory().ReserveName(typename))
				{
					TypeBuilder tb = mod.DefineType(typename, TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.ExplicitLayout | TypeAttributes.NotPublic, Types.ValueType, PackingSize.Size1, length);
					AttributeHelper.HideFromJava(tb);
					type = tb.CreateType();
				}
			}
			if (type == null
				|| !type.IsValueType
				|| type.StructLayoutAttribute.Pack != 1 || type.StructLayoutAttribute.Size != length)
			{
				// the type that we found doesn't match (must mean we've compiled a Java type with that name),
				// so we fall back to the string approach
				ilgen.Emit(OpCodes.Ldstr, str);
				ilgen.Emit(OpCodes.Call, Types.String.GetMethod("ToCharArray", Type.EmptyTypes));
				return;
			}
			ilgen.Emit(OpCodes.Ldc_I4, str.Length);
			ilgen.Emit(OpCodes.Newarr, Types.Char);
			ilgen.Emit(OpCodes.Dup);
			byte[] data = new byte[length];
			for (int j = 0; j < str.Length; j++)
			{
				data[j * 2 + 0] = (byte)(str[j] >> 0);
				data[j * 2 + 1] = (byte)(str[j] >> 8);
			}
			// NOTE we define a module field, because type fields on Mono don't use the global $ArrayType$<n> type.
			// NOTE this also means that this will only work during static compilation, because ModuleBuilder.CreateGlobalFunctions() must
			// be called before the field can be used.
			FieldBuilder fb = mod.DefineInitializedData("__<str>", data, FieldAttributes.Static | FieldAttributes.PrivateScope);
			// MONOBUG Type.Equals(Type) is broken on Mono. We have to use the virtual method that ends up in our implementation
			if (!fb.FieldType.Equals((object)type))
			{
				// this is actually relatively harmless, but I would like to know about it, so we abort and hope that users report this when they encounter it
				JVM.CriticalFailure("Unsupported runtime: ModuleBuilder.DefineInitializedData() field type mispredicted", null);
			}
			ilgen.Emit(OpCodes.Ldtoken, fb);
			ilgen.Emit(OpCodes.Call, JVM.Import(typeof(System.Runtime.CompilerServices.RuntimeHelpers)).GetMethod("InitializeArray", new Type[] { Types.Array, JVM.Import(typeof(RuntimeFieldHandle)) }));
		}

		private static bool Reflection_getCallerClass(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			if (caller.HasCallerID
				&& opcodeIndex > 0
				&& (flags[opcodeIndex - 1] & InstructionFlags.BranchTarget) == 0
				&& code[opcodeIndex - 1].NormalizedOpCode == NormalizedByteCode.__iconst
				&& code[opcodeIndex - 1].Arg1 == 2)
			{
				ilgen.LazyEmitPop();
				int arg = caller.GetParametersForDefineMethod().Length - 1;
				if (!caller.IsStatic)
				{
					arg++;
				}
				ilgen.Emit(OpCodes.Ldarg, (short)arg);
				MethodWrapper mw = CoreClasses.ikvm.@internal.CallerID.Wrapper.GetMethodWrapper("getCallerClass", "()Ljava.lang.Class;", false);
				mw.Link();
				mw.EmitCallvirt(ilgen);
				return true;
			}
			return false;
		}

		private static bool ClassLoader_getCallerClassLoader(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			if (caller.HasCallerID)
			{
				int arg = caller.GetParametersForDefineMethod().Length - 1;
				if (!caller.IsStatic)
				{
					arg++;
				}
				ilgen.Emit(OpCodes.Ldarg, (short)arg);
				MethodWrapper mw = CoreClasses.ikvm.@internal.CallerID.Wrapper.GetMethodWrapper("getCallerClassLoader", "()Ljava.lang.ClassLoader;", false);
				mw.Link();
				mw.EmitCallvirt(ilgen);
				return true;
			}
			return false;
		}

		private static bool CallerID_getCallerID(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			if (caller.HasCallerID)
			{
				int arg = caller.GetParametersForDefineMethod().Length - 1;
				if (!caller.IsStatic)
				{
					arg++;
				}
				ilgen.Emit(OpCodes.Ldarg, (short)arg);
				return true;
			}
			else
			{
				JVM.CriticalFailure("CallerID.getCallerID() requires a HasCallerID annotation", null);
			}
			return false;
		}

		private static bool Util_getInstanceTypeFromClass(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			TypeWrapper tw = ilgen.PeekLazyClassLiteral();
			if (tw != null)
			{
				ilgen.LazyEmitPop();
				if (tw.IsRemapped && tw.IsFinal)
				{
					ilgen.Emit(OpCodes.Ldtoken, tw.TypeAsTBD);
				}
				else
				{
					ilgen.Emit(OpCodes.Ldtoken, tw.TypeAsBaseType);
				}
				ilgen.Emit(OpCodes.Call, Compiler.getTypeFromHandleMethod);
				return true;
			}
			return false;
		}

#if STATIC_COMPILER
		private static bool Class_getPrimitiveClass(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			ilgen.LazyEmitPop();
			ilgen.Emit(OpCodes.Ldnull);
			MethodWrapper mw = CoreClasses.java.lang.Class.Wrapper.GetMethodWrapper("<init>", "(Lcli.System.Type;)V", false);
			mw.Link();
			mw.EmitNewobj(ilgen);
			return true;
		}
#endif

		private static bool ThreadLocal_new(DynamicTypeWrapper.FinishContext context, CodeEmitter ilgen, MethodWrapper method, MethodAnalyzer ma, int opcodeIndex, MethodWrapper caller, ClassFile classFile, Instruction[] code, InstructionFlags[] flags)
		{
			// it is only valid to replace a ThreadLocal instantiation by our ThreadStatic based version, if we can prove that the instantiation only happens once
			// (which is the case when we're in <clinit> and there aren't any branches that lead to the current position)
			if (caller.Name != StringConstants.CLINIT)
			{
				return false;
			}
#if CLASSGC
			if (JVM.classUnloading)
			{
				// RunAndCollect assemblies do not support ThreadStaticAttribute
				return false;
			}
#endif
			for (int i = 0; i <= opcodeIndex; i++)
			{
				if ((flags[i] & InstructionFlags.BranchTarget) != 0)
				{
					return false;
				}
			}
			ilgen.Emit(OpCodes.Newobj, DefineThreadLocalType(context, opcodeIndex, caller));
			return true;
		}

		private static ConstructorBuilder DefineThreadLocalType(DynamicTypeWrapper.FinishContext context, int opcodeIndex, MethodWrapper caller)
		{
			TypeWrapper threadLocal = ClassLoaderWrapper.LoadClassCritical("ikvm.internal.IntrinsicThreadLocal");
			TypeBuilder tb = caller.DeclaringType.TypeAsBuilder.DefineNestedType("__<tls>_" + opcodeIndex, TypeAttributes.NestedPrivate | TypeAttributes.Sealed, threadLocal.TypeAsBaseType);
			FieldBuilder fb = tb.DefineField("field", Types.Object, FieldAttributes.Private | FieldAttributes.Static);
			fb.SetCustomAttribute(new CustomAttributeBuilder(JVM.Import(typeof(ThreadStaticAttribute)).GetConstructor(Type.EmptyTypes), new object[0]));
			MethodBuilder mbGet = tb.DefineMethod("get", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final, Types.Object, Type.EmptyTypes);
			ILGenerator ilgen = mbGet.GetILGenerator();
			ilgen.Emit(OpCodes.Ldsfld, fb);
			ilgen.Emit(OpCodes.Ret);
			MethodBuilder mbSet = tb.DefineMethod("set", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final, null, new Type[] { Types.Object });
			ilgen = mbSet.GetILGenerator();
			ilgen.Emit(OpCodes.Ldarg_1);
			ilgen.Emit(OpCodes.Stsfld, fb);
			ilgen.Emit(OpCodes.Ret);
			ConstructorBuilder cb = tb.DefineConstructor(MethodAttributes.Assembly, CallingConventions.Standard, Type.EmptyTypes);
			CodeEmitter ctorilgen = CodeEmitter.Create(cb);
			ctorilgen.Emit(OpCodes.Ldarg_0);
			MethodWrapper basector = threadLocal.GetMethodWrapper("<init>", "()V", false);
			basector.Link();
			basector.EmitCall(ctorilgen);
			ctorilgen.Emit(OpCodes.Ret);
			context.RegisterPostFinishProc(delegate
			{
				threadLocal.Finish();
				tb.CreateType();
			});
			return cb;
		}
	}
}
