/*
  Copyright (C) 2007 Jeroen Frijters

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
using System.Reflection;
using System.Reflection.Emit;
using IKVM.Internal;

static class AtomicReferenceFieldUpdaterEmitter
{
	private static readonly string ClassName = string.Intern("java.util.concurrent.atomic.AtomicReferenceFieldUpdater");
	private static readonly string MethodName = string.Intern("newUpdater");
	private static readonly string MethodSignature = string.Intern("(Ljava.lang.Class;Ljava.lang.Class;Ljava.lang.String;)Ljava.util.concurrent.atomic.AtomicReferenceFieldUpdater;");
	private static readonly Dictionary<FieldWrapper, ConstructorBuilder> map = new Dictionary<FieldWrapper, ConstructorBuilder>();

	internal static bool Emit(TypeWrapper wrapper, CountingILGenerator ilgen, ClassFile classFile, ClassFile.ConstantPoolItemMI cpi, int i, ClassFile.Method.Instruction[] code)
	{
		if (ReferenceEquals(cpi.Class, AtomicReferenceFieldUpdaterEmitter.ClassName)
			&& ReferenceEquals(cpi.Name, AtomicReferenceFieldUpdaterEmitter.MethodName)
			&& ReferenceEquals(cpi.Signature, AtomicReferenceFieldUpdaterEmitter.MethodSignature)
			&& i >= 3
			&& !code[i - 0].IsBranchTarget
			&& !code[i - 1].IsBranchTarget
			&& !code[i - 2].IsBranchTarget
			&& code[i - 1].NormalizedOpCode == NormalizedByteCode.__ldc
			&& code[i - 2].NormalizedOpCode == NormalizedByteCode.__ldc
			&& code[i - 3].NormalizedOpCode == NormalizedByteCode.__ldc)
		{
			// we now have a structural match, now we need to make sure that the argument values are what we expect
			TypeWrapper tclass = classFile.GetConstantPoolClassType(code[i - 3].Arg1);
			TypeWrapper vclass = classFile.GetConstantPoolClassType(code[i - 2].Arg1);
			string fieldName = classFile.GetConstantPoolConstantString(code[i - 1].Arg1);
			if (tclass == wrapper && !vclass.IsUnloadable && !vclass.IsPrimitive && !vclass.IsNonPrimitiveValueType)
			{
				FieldWrapper field = wrapper.GetFieldWrapper(fieldName, vclass.SigName);
				if (field != null && !field.IsStatic && field.IsVolatile && field.DeclaringType == wrapper && field.FieldTypeWrapper == vclass)
				{
					// everything matches up, now call the actual emitter
					DoEmit(wrapper, ilgen, field);
					return true;
				}
			}
		}
		return false;
	}

	private static void DoEmit(TypeWrapper wrapper, CountingILGenerator ilgen, FieldWrapper field)
	{
		ConstructorBuilder cb;
		bool exists;
		lock (map)
		{
			exists = map.TryGetValue(field, out cb);
		}
		if (!exists)
		{
			// note that we don't need to lock here, because we're running as part of FinishCore, which is already protected by a lock
			TypeWrapper arfuTypeWrapper = ClassLoaderWrapper.LoadClassCritical(ClassName);
			TypeBuilder tb = wrapper.TypeAsBuilder.DefineNestedType("__ARFU_" + field.Name + field.Signature.Replace('.', '/'), TypeAttributes.NestedPrivate | TypeAttributes.Sealed, arfuTypeWrapper.TypeAsBaseType);
			EmitCompareAndSet("compareAndSet", tb, field.GetField());
			EmitCompareAndSet("weakCompareAndSet", tb, field.GetField());
			EmitGet(tb, field.GetField());
			EmitSet("set", tb, field.GetField(), false);
			EmitSet("lazySet", tb, field.GetField(), true);

			cb = tb.DefineConstructor(MethodAttributes.Assembly, CallingConventions.Standard, Type.EmptyTypes);
			lock (map)
			{
				map.Add(field, cb);
			}
			CountingILGenerator ctorilgen = cb.GetILGenerator();
			ctorilgen.Emit(OpCodes.Ldarg_0);
			MethodWrapper basector = arfuTypeWrapper.GetMethodWrapper("<init>", "()V", false);
			basector.Link();
			basector.EmitCall(ctorilgen);
			ctorilgen.Emit(OpCodes.Ret);
			((DynamicTypeWrapper)wrapper).RegisterPostFinishProc(delegate
			{
				arfuTypeWrapper.Finish();
				tb.CreateType();
			});
		}
		ilgen.Emit(OpCodes.Pop);
		ilgen.Emit(OpCodes.Pop);
		ilgen.Emit(OpCodes.Pop);
		ilgen.Emit(OpCodes.Newobj, cb);
	}

	private static void EmitCompareAndSet(string name, TypeBuilder tb, FieldInfo field)
	{
		MethodBuilder compareAndSet = tb.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual, typeof(bool), new Type[] { typeof(object), typeof(object), typeof(object) });
		ILGenerator ilgen = compareAndSet.GetILGenerator();
		ilgen.Emit(OpCodes.Ldarg_1);
		ilgen.Emit(OpCodes.Castclass, field.DeclaringType);
		ilgen.Emit(OpCodes.Ldflda, field);
		ilgen.Emit(OpCodes.Ldarg_3);
		ilgen.Emit(OpCodes.Castclass, field.FieldType);
		ilgen.Emit(OpCodes.Ldarg_2);
		ilgen.Emit(OpCodes.Castclass, field.FieldType);
		MethodInfo interlockedCompareExchange = null;
		foreach (MethodInfo m in typeof(System.Threading.Interlocked).GetMethods())
		{
			if (m.Name == "CompareExchange" && m.IsGenericMethodDefinition)
			{
				interlockedCompareExchange = m;
				break;
			}
		}
		ilgen.Emit(OpCodes.Call, interlockedCompareExchange.MakeGenericMethod(field.FieldType));
		ilgen.Emit(OpCodes.Ldarg_2);
		ilgen.Emit(OpCodes.Ceq);
		ilgen.Emit(OpCodes.Ret);
	}

	private static void EmitGet(TypeBuilder tb, FieldInfo field)
	{
		MethodBuilder get = tb.DefineMethod("get", MethodAttributes.Public | MethodAttributes.Virtual, typeof(object), new Type[] { typeof(object) });
		ILGenerator ilgen = get.GetILGenerator();
		ilgen.Emit(OpCodes.Ldarg_1);
		ilgen.Emit(OpCodes.Castclass, field.DeclaringType);
		ilgen.Emit(OpCodes.Volatile);
		ilgen.Emit(OpCodes.Ldfld, field);
		ilgen.Emit(OpCodes.Ret);
	}

	private static void EmitSet(string name, TypeBuilder tb, FieldInfo field, bool lazy)
	{
		MethodBuilder set = tb.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual, typeof(void), new Type[] { typeof(object), typeof(object) });
		ILGenerator ilgen = set.GetILGenerator();
		ilgen.Emit(OpCodes.Ldarg_1);
		ilgen.Emit(OpCodes.Castclass, field.DeclaringType);
		ilgen.Emit(OpCodes.Ldarg_2);
		ilgen.Emit(OpCodes.Castclass, field.FieldType);
		if (!lazy)
		{
			ilgen.Emit(OpCodes.Volatile);
		}
		ilgen.Emit(OpCodes.Stfld, field);
		ilgen.Emit(OpCodes.Ret);
	}
}