﻿namespace Adaptify


#if FABLE_COMPILER

module private ShallowEqualityHelpers =
    open Fable.Core
    open Fable.Core.JsInterop

    [<Emit("$0 === $1")>]
    let equals (a : 'a) (b : 'a) : bool = jsNative

    let inline hash (a : 'a) = (a :> obj).GetHashCode()

type ShallowEqualityComparer<'a> private() =
    static let instance = ShallowEqualityComparer<'a>() :> System.Collections.Generic.IEqualityComparer<'a>

    static member Instance = instance

    static member ShallowHashCode v = ShallowEqualityHelpers.hash v
    static member ShallowEquals(a,b) = ShallowEqualityHelpers.equals a b

    interface System.Collections.Generic.IEqualityComparer<'a> with
        member x.GetHashCode v = ShallowEqualityHelpers.hash v
        member x.Equals(a,b) = ShallowEqualityHelpers.equals a b

#else
open System.Reflection.Emit
open System.Reflection
open System.Runtime.InteropServices
open System.Runtime.CompilerServices

type private EqDelegate<'a> = delegate of 'a * 'a -> bool
type private HashDelegate<'a> = delegate of 'a -> int

type private HashCode =
    static member Combine(a : int, b : int) =   
        uint32 a ^^^ uint32 b + 0x9e3779b9u + ((uint32 a) <<< 6) + ((uint32 a) >>> 2) |> int

type ShallowEqualityComparer<'a> private() =
    static let typ = typeof<'a>
    static let self = typedefof<ShallowEqualityComparer<_>>

    static let combineMeth = 
        typeof<HashCode>.GetMethod(
            "Combine", 
            BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic,
            System.Type.DefaultBinder,
            [| typeof<int>; typeof<int> |],
            null
        )

    static let isUnmanaged =
        if typ.IsValueType then
            let arr : 'a[] = [|Unchecked.defaultof<'a>|]
            try
                let g = GCHandle.Alloc(arr, GCHandleType.Pinned)
                g.Free()
                true
            with _ ->
                false
        else
            false

    static let getHashCode =
        if isUnmanaged then
            Unchecked.hash
        elif typ.IsValueType then
            let fields =
                typ.GetFields(BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public)
                
            let meth = 
                DynamicMethod(
                    "shallowHash", 
                    MethodAttributes.Static ||| MethodAttributes.Public,
                    CallingConventions.Standard,
                    typeof<int>,
                    [| typeof<'a> |],
                    typeof<'a>,
                    true
                )


            let il = meth.GetILGenerator()
            let l = il.DeclareLocal(typeof<int>)

            // l <- 0
            il.Emit(OpCodes.Ldc_I4_0)
            il.Emit(OpCodes.Stloc, l)

            for f in fields do
                let self = self.MakeGenericType [| f.FieldType |]
                let hash = 
                    self.GetMethod(
                        "ShallowHashCode", 
                        BindingFlags.Static ||| BindingFlags.NonPublic ||| BindingFlags.Public, 
                        System.Type.DefaultBinder, 
                        [| f.FieldType |], 
                        null
                    )

                il.Emit(OpCodes.Ldarg_0)
                il.Emit(OpCodes.Ldfld, f)
                il.EmitCall(OpCodes.Call, hash, null)
                il.Emit(OpCodes.Ldloc, l)
                il.EmitCall(OpCodes.Call, combineMeth, null)
                il.Emit(OpCodes.Stloc, l)
                    
            il.Emit(OpCodes.Ldloc, l)
            il.Emit(OpCodes.Ret)

            let del = meth.CreateDelegate(typeof<HashDelegate<'a>>) |> unbox<HashDelegate<'a>>
            del.Invoke
        else
            fun (v : 'a) -> RuntimeHelpers.GetHashCode(v :> obj)

    static let equals =
        if isUnmanaged then
            Unchecked.equals
        elif typ.IsValueType then
            let fields =
                typ.GetFields(BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public)

            let meth = 
                DynamicMethod(
                    "shallowEquals", 
                    MethodAttributes.Static ||| MethodAttributes.Public,
                    CallingConventions.Standard,
                    typeof<bool>,
                    [| typeof<'a>; typeof<'a> |],
                    typeof<'a>,
                    true
                )

            let il = meth.GetILGenerator()
            let falseLabel = il.DefineLabel()

            for f in fields do
                let self = self.MakeGenericType [| f.FieldType |]
                let eq = 
                    self.GetMethod(
                        "ShallowEquals", 
                        BindingFlags.Static ||| BindingFlags.NonPublic ||| BindingFlags.Public, 
                        System.Type.DefaultBinder, 
                        [| f.FieldType; f.FieldType |], 
                        null
                    )

                il.Emit(OpCodes.Ldarg_0)
                il.Emit(OpCodes.Ldfld, f)
                    
                il.Emit(OpCodes.Ldarg_1)
                il.Emit(OpCodes.Ldfld, f)

                il.EmitCall(OpCodes.Call, eq, null)
                il.Emit(OpCodes.Brfalse, falseLabel)

            il.Emit(OpCodes.Ldc_I4_1)
            il.Emit(OpCodes.Ret)
            il.MarkLabel(falseLabel)
            il.Emit(OpCodes.Ldc_I4_0)
            il.Emit(OpCodes.Ret)

            let del = meth.CreateDelegate(typeof<EqDelegate<'a>>) |> unbox<EqDelegate<'a>>
            fun (a : 'a) (b : 'a) -> del.Invoke(a, b)
        else
            fun (a : 'a) (b : 'a) -> System.Object.ReferenceEquals(a :> obj, b :> obj)
            
    static let instance = ShallowEqualityComparer<'a>() :> System.Collections.Generic.IEqualityComparer<'a>

    static member Instance = instance

    static member ShallowHashCode v = getHashCode v
    static member ShallowEquals(a,b) = equals a b

    interface System.Collections.Generic.IEqualityComparer<'a> with
        member x.GetHashCode v = getHashCode v
        member x.Equals(a,b) = equals a b

#endif
