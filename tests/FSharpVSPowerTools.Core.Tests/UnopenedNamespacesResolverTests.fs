﻿module FSharpVSPowerTools.Core.Tests.UnopenedNamespacesResolverTests

open NUnit.Framework
open FSharpVSPowerTools

let (=>) (ns: string option, scope: string, currentIdent, requireQualifiedAccessParent: string option, entityNs: string option, 
          entityFullName: string) res = 
    Entity.tryCreate 
            (ns |> Option.map (fun x -> x.Split '.'))
            (scope.Split '.') 
            currentIdent 
            (requireQualifiedAccessParent |> Option.map (fun x -> x.Split '.')) 
            (entityNs |> Option.map (fun x -> x.Split '.'))
            (entityFullName.Split '.')
    |> assertEqual (res |> Option.map (fun (ns, name) -> { Namespace = ns; Name = name }))

[<Test>] 
let ``fully qualified external entities``() =
    (Some "TopNs", "", "Now", None, Some "System", "System.DateTime.Now") => Some (Some "System.DateTime", "Now")
    (Some "TopNs", "", "Now", None, None, "System.Now") => Some (Some "System", "Now")
    (Some "Myns", "Myns", "Now", None, None, "System.Now") => Some (Some "System", "Now")
    (Some "Myns", "Myns.Nested", "Now", None, None, "System.Now") => Some (Some "System", "Now")

[<Test>] 
let ``fully qualified external entities with require qualified access module``() =
    (Some "TopNs", "", "Now", Some "System", Some "System", "System.DateTime.Now") => Some (None, "System.DateTime.Now")
    (Some "TopNs", "", "Now", Some "System.DateTime", Some "System",  "System.DateTime.Now") => Some (Some "System", "DateTime.Now")

[<Test>]
let ``simple entities``() =
    (Some "TopNs", "", "Now", None, None, "Now") => None
    (Some "Myns", "Myns", "Now", None, None, "Now") => None

[<Test>]
let ``internal entities``() =
    (Some "Myns", "Myns", "Now", None, Some "Myns", "Myns.Nested.Now") => Some (Some "Nested", "Now")   
    (Some "Myns", "Myns.Nested", "Now", None, Some "Myns", "Myns.Nested.Nested2.Now") => Some (Some "Nested2", "Now")
    (Some "Myns", "Myns.Nested", "Now", None, Some "Myns", "Myns.Nested.Now") => None

[<Test>]
let ``internal entities in different sub namespace``() =
    (Some "Myns.Nested1", "Myns.Nested1", "Now", None, Some "Myns.Nested2", "Myns.Nested2.Now") => Some (Some "Myns.Nested2", "Now")   

[<Test>] 
let ``internal entities with require qualified access module``() =
    (Some "Myns", "Myns", "Now", Some "Myns.Nested", Some "Myns", "Myns.Nested.Now") => Some (None, "Nested.Now")   

open FSharpVSPowerTools.Core.Tests.CodeGenerationTestInfrastructure 

let file = "C:\\file.fs"
let languageService = LanguageService(fun _ -> ())

type Source = string

let parseSource (source: Source) =
    let parseResult = 
        languageService.ParseFileInProject(LanguageServiceTestHelper.projectOptions file, file, source) 
        |> Async.RunSynchronously
    if parseResult.ParseHadErrors then failwithf "Cannot parse input: %s, errors: %A" source parseResult.Errors
    match parseResult.ParseTree with
    | None -> failwithf "ParseTree is None for input: %s" source
    | Some tree -> tree

open Microsoft.FSharp.Compiler.Range

type Line = int
type Col = int

let checkEntity assertion source (points: (Line * Col) list) =
    for line, col in points do
        let tree = parseSource source
        try
            assertion (
                Ast.getEntityKind tree (Pos.fromZ line col), 
                sprintf "Line = %d, Col = %d" line col)
        with _ ->
            printfn "Ast: %A" tree
            reraise()

let (==>) = checkEntity (fun (kind, _) -> Assert.IsTrue kind.IsSome)
let (!=>) = checkEntity (fun (kind, _) -> Assert.IsTrue kind.IsNone)

[<Test>]
let ``return type annotation is an entity``() =
    """
module TopLevel
let x: DateTime = ()
type T() =
    let field: DateTime option = None
    member x.Prop: DateTime option = None
    member x.Method(): DateTime option = None
""" 
    ==> [2, 9; 4, 17; 5, 21; 6, 25]

[<Test>]
let ``type name in expression is an entity``() =
    """
module TopLevel
let x = DateTime.Now
type T() =
    let field = DateTime.Now
    member x.Prop = DateTime.Now
    member x.Method() = DateTime.Now
""" 
    ==> [2, 10; 4, 18; 5, 22; 6, 26]

[<Test>]
let ``argument type annotation is an entity``() =
    """
module TopLevel
let f (arg: DateTime) = ()
type T() =
    member x.Method (arg1: DateTime, arg2: TimeSpan) = ()
""" 
    ==> [2, 15; 4, 29; 4, 45]

[<Test>]
let ``attribute is an entity``() =
    """
module TopLevel
let f ([<Attribute>] arg: DateTime) = ()
[<Attribute>]
type T() =
    [<Attribute>]
    member x.Prop = ()
    [<Attribute>] static member StaticMember ([<Attribute>] arg) = ()
[<Attribute>]
type R = { [<Attribute>] F: int }
""" 
    ==> [2, 11; 3, 4; 5, 8; 7, 8; 7, 51; 8, 4; 9, 15]

[<Test>]
let ``type in an object expression method body is an entity``() =
    """
module TopLevel
let _ = { new IMy with 
            method x.Method x = DateTime.Now }
""" 
    ==> [3, 34]

[<Test>]
let ``type in a let binging inside CE``() =
    """
module TopLevel
let _ = 
    async { 
        let _ = DateTime.Now
        return () 
    }
""" 
    ==> [4, 18]

[<Test>]
let ``type as a qualifier in a function application in argument position``() =
    """
module TopLevel
let _ = func (DateTime.Add 1)
let _ = func1 1 (2, DateTime.Add 1)
""" 
    ==> [2, 16; 3, 22]

[<Test>]
let ``constructor as argument``() =
    """
module TopLevel
let _ = x.func (DateTime())
""" 
    ==> [2, 17]

[<Test>]
let ``type in match``() =
    """
module TopLevel
let _ = 
    match 1 with
    | Case1 -> DateTime.Now
""" 
    ==> [4, 17]

[<Test>]
let ``generic type is an entity``() =
    """
module TopLevel
let _ = Class<DateTime>()
type R = {
    Field: Task<_>
}
""" 
    ==> [2, 9; 4, 12]

[<Test>]
let ``generic type argument is an entity``() =
    """
module TopLevel
let _ = Class<DateTime>()
""" 
    ==> [2, 15]

[<Test>]
let ``upcast type is an entity``() =
    """
module TopLevel
let x: IMy<_, _, _> = upcast My(arg)
type T() =
    let x: IMy<_, _, _> = upcast My(arg)
""" 
    ==> [2, 30; 4, 34]

[<Test>]
let ``open declaration is not an entity``() =
    """
module TopLevel
open System.Threading.Tasks
module M =
    open System
    let () = ()
""" 
    !=> [2, 5; 2, 13; 2, 24; 4, 11]
    
[<Test>]
let ``module value is not an entity``() =
    """
module TopLevel
let value = ()
""" 
    !=> [2, 6]

[<Test>]
let ``class member is not an entity``() =
    """
module TopLevel
type C() =
    member x.Member x = ()
    member x.Prop = ()
""" 
    !=> [3, 15; 4, 15]

[<Test>]
let ``type or module name is not an entity``() =
    """
module TopLevel
type Class() = class end
module Nested =
    type Record = { F: int }
""" 
    !=> [1, 9; 2, 7; 3, 9; 4, 11]

[<Test; Ignore "Cannot extract arg name from Named">]
let ``argument name is not an entity``() =
    """
module TopLevel
let func (arg: int) = ()
type Class() =
    let func (arg: int) = ()
    member x.Method (arg: int) = ()
""" 
    !=> [2, 12; 4, 18; 5, 25]

[<Test>]
let ``wildcard generic type argument is not an entity``() =
    """
module TopLevel
let _ = Class<_>()
""" 
    !=> [2, 15]

let forLine (line: Line) (source: Source) = source, line
let forIdent ident (source, line) = ident, source, line

let forEntity (ns: LongIdent) (entity: LongIdent) (ident, source: Source, line) =
    let tree = parseSource source
    match Ast.tryFindNearestOpenStatementBlock line tree ident (None, Some (ns.Split '.'), entity.Split '.') with
    | None -> failwith "Cannot find nearest open statement block"
    | Some (e, pos) -> source, e, pos

let result (expected: Source) (source: Source, entity, pos) = 
    let lines = srcToLineArray source
    let line = pos.Line - 1
    if lines.Length < line + 1 then 
        failwithf "Pos.Line = %d is out of bound (source contain %d lines)" pos.Line lines.Length
    let result = 
        Array.append (
            Array.append 
                lines.[0..line - 1] 
                [| (String.replicate pos.Col " ") + "open " + entity.Namespace.Value|]) 
            lines.[line..]
    try result |> Collection.assertEqual (srcToLineArray expected)
    with _ ->
        let withLineNumbers xs = 
            xs
            |> List.mapi (fun i x -> sprintf "%d: %s" i x)
            |> String.concat "\r\n"

        printfn 
            "Expected:\n%s\nActual:\n%s" 
            (expected |> srcToLineArray |> Array.toList |> withLineNumbers) 
            (result |> Array.toList |> withLineNumbers)
        reraise()

[<Test>]
let ``external top level symbol, no other open declarations``() =
    """
module TopLevel

let _ = DateTime.Now
"""
    |> forLine 3
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
module TopLevel
open System

let _ = DateTime.Now
"""

[<Test>]
let ``external top level symbol, another open declaration is present``() =
    """
module TopLevel

open Another

let _ = DateTime.Now
"""
    |> forLine 5
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
module TopLevel

open Another
open System

let _ = DateTime.Now
"""

[<Test>]
let ``external top level symbol, other open declarations are present``() =
    """
module TopLevel

open Another
open OneMore

let _ = DateTime.Now
"""
    |> forLine 6
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
module TopLevel

open Another
open OneMore
open System

let _ = DateTime.Now
"""

[<Test>]
let ``external symbol in a nested module, no other open declarations``() =
    """
module TopLevel

module Nested =
    let _ = DateTime.Now
"""
    |> forLine 4
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
module TopLevel

module Nested =
    open System
    let _ = DateTime.Now
"""

[<Test>]
let ``external symbol in a nested module, another open declaration is present``() =
    """
module TopLevel

module Nested =
    open Another
    let _ = DateTime.Now
"""
    |> forLine 5
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
module TopLevel

module Nested =
    open Another
    open System
    let _ = DateTime.Now
"""

[<Test>]
let ``external symbol in a nested module, other open declarations are present``() =
    """
module TopLevel

module Nested =
    open Another
    open OneMore

    let _ = DateTime.Now
"""
    |> forLine 7
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
module TopLevel

module Nested =
    open Another
    open OneMore
    open System

    let _ = DateTime.Now
"""

[<Test>]
let ``external symbol in a double nested module, no other open declarations are present``() =
    """
module TopLevel

module Nested =
    module DoubleNested =
        let _ = DateTime.Now
"""
    |> forLine 5
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
module TopLevel

module Nested =
    module DoubleNested =
        open System
        let _ = DateTime.Now
"""

[<Test>]
let ``external symbol in a double nested module, other open declarations on all levels are present``() =
    """
module TopLevel

open Another

module Nested =
    open OneMore

    module DoubleNested =
        open OneMore1

        let _ = DateTime.Now
"""
    |> forLine 11
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
module TopLevel

open Another

module Nested =
    open OneMore

    module DoubleNested =
        open OneMore1
        open System

        let _ = DateTime.Now
"""

[<Test>]
let ``top level symbol declared in a nested module in the same file``() =
    """
module TopLevel

module Nested =
    type DateTime() = class end

let marker = ()
let _ = DateTime.Now
"""
    |> forLine 6
    |> forIdent "DateTime"
    |> forEntity "" "TopLevel.Nested.DateTime"
    |> result """
module TopLevel

module Nested =
    type DateTime() = class end
open Nested

let marker = ()
let _ = DateTime.Now
"""

[<Test>]
let ``top level symbol declared in a nested module in the same file, there is another module below``() =
    """
module TopLevel

module Nested =
    type DateTime() = class end

let _ = DateTime.Now

module Below =
    let x = ()
"""
    |> forLine 6
    |> forIdent "DateTime"
    |> forEntity "" "TopLevel.Nested.DateTime"
    |> result """
module TopLevel

module Nested =
    type DateTime() = class end
open Nested

let _ = DateTime.Now

module Below =
    let x = ()
"""

[<Test>]
let ``symbol declared in another module in the same namespace``() =
    """
namespace TopNs

module Nested =
    type DateTime() = class end

module Another =
    let _ = DateTime.Now
"""
    |> forLine 7
    |> forIdent "DateTime"
    |> forEntity "TopNs" "TopNs.Nested.DateTime"
    |> result """
namespace TopNs

module Nested =
    type DateTime() = class end
open Nested

module Another =
    let _ = DateTime.Now
"""

[<Test>]
let ``symbol declared in a top level record in a namespace, no other open statements``() =
    """
namespace TopNs

type Record = 
    { F: DateTime }
"""
    |> forLine 4
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
namespace TopNs

open System
type Record = 
    { F: DateTime }
"""

[<Test>]
let ``symbol declared in a top level record in a namespace, there's another open statement``() =
    """
namespace TopNs

open Another

type Record = 
    { F: DateTime }
"""
    |> forLine 6
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
namespace TopNs

open Another
open System

type Record = 
    { F: DateTime }
"""

[<Test>]
let ``respects existing open statements identation``() =
    """
namespace TopNs

  open Another

  type Record = 
      { F: DateTime }
"""
    |> forLine 6
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
namespace TopNs

  open Another
  open System

  type Record = 
      { F: DateTime }
"""


[<Test>]
let ``respects top level block identation in case where are no other open statements``() =
    """
namespace TopNs

 type Record = 
   { F: DateTime }
"""
    |> forLine 6
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
namespace TopNs

 open System
 type Record = 
   { F: DateTime }
"""

[<Test>]
let ``respects block identation in case where are no other open statements``() =
    """
namespace TopNs

module M =
 type Record = 
   { F: DateTime }
"""
    |> forLine 6
    |> forIdent "DateTime"
    |> forEntity "System" "System.DateTime"
    |> result """
namespace TopNs

module M =
 open System
 type Record = 
   { F: DateTime }
"""
