﻿// Learn more about F// at http://fsharp.org

open Types
open SpecificationOpenGL
open TypeMapping
open Util
open Constants

open System
open System.IO
open System.Text.RegularExpressions

let readSpec (path:string) = OpenGL_Specification.Load path

let getEnumsFromSpecification (spec: OpenGL_Specification.Registry) =
    let valueForName =
        spec.Enums
        |> Array.Parallel.collect(fun e ->
            e.Enums
            |> Array.Parallel.choose(fun case ->
                case.Value.String
                |> Option.map(fun value -> (case.Name, value)))
        )
        |> Map.ofArray

    spec.Groups
    |> Array.Parallel.choose(fun e ->
        maybe {
            let group = e.Name
            let cases =
                e.Enums
                |> Array.Parallel.choose(fun case ->
                    valueForName
                    |> Map.tryFind case.Name
                    |> Option.map(fun value ->
                        { name = case.Name
                          value =  value })
                )
                |> Array.groupBy(fun case -> case.value)
                |> Array.Parallel.choose(fun (_, cases) ->
                    cases
                    |> Array.tryFind(fun case -> (case.name.Contains "EXT" || case.name.Contains "NV") |> not)
                    |> Option.orElse (cases |> Array.tryHead)
                )
            let res =
                { groupName = group
                  cases = cases }
            return! Some res
        }
    )

let paramsTypeRegex = new Regex("<param(.*?(group=\"(?<group>.*?)\").*?|.*?)>(?<f>.*?)(<ptype>(?<t>.*?)<\/ptype>(?<b>.*?))?(<name>.+?<\/name>)<\/param>")
let protoTypeRegex = new Regex("<proto(.*?(group=\"(?<group>.*?)\").*?|.*?)>(?<f>.*?)(<ptype>(?<t>.*?)<\/ptype>(?<b>.*?))?(<name>.+?<\/name>)<\/proto>")
let getGroupValue (group: string) (matching: Match) =
    matching.Groups.[group].Value

let getRawElementDataWithoutLineBreaks (v: Xml.Linq.XElement) =
    v.ToString().Replace("\n", "").Replace("\t", "").Replace("\r", "")

let extractTypeFromPtype (param: OpenGL_Specification.Param) =
    let str = getRawElementDataWithoutLineBreaks param.XElement
    let res = paramsTypeRegex.Match str
    let groupName =
        let str = getGroupValue "group" res
        if str |> String.IsNullOrWhiteSpace then None
        else Some str
    let ty =
        getGroupValue "f" res +
        getGroupValue "t" res +
        getGroupValue "b" res
    groupName, ty

let extractTypeFromProto (proto: OpenGL_Specification.Proto) =
    let str = getRawElementDataWithoutLineBreaks proto.XElement
    let res = protoTypeRegex.Match str
    let groupName =
        let str = getGroupValue "group" res
        if str |> String.IsNullOrWhiteSpace then None
        else Some str
    let ty =
        getGroupValue "f" res +
        getGroupValue "t" res +
        getGroupValue "b" res
    groupName, ty

let getFunctions (spec: OpenGL_Specification.Registry) =
    spec.Commands.Commands
    |> Array.Parallel.map(fun cmd ->
        //let isInvalid =
        //    cmd.Params |> Array.exists(fun p -> p.Ptype |> Option.isNone)
        //    || Option.isNone cmd.Proto.Ptype 
        //if isInvalid then
        //    printfn "Function specification %A is invalid" cmd.Proto.Name
        //    cmd.Params |> Seq.iter (fun p -> printfn "%A" p.XElement.Value)
        //    None
        //else
            let funcName = cmd.Proto.Name
            let parameters =
                cmd.Params
                |> Array.Parallel.map(fun p ->
                    let group, ty = extractTypeFromPtype p
                    if String.IsNullOrWhiteSpace ty then
                        let str = p.XElement.ToString()
                        printfn "failed parsing %A, value: %s" (funcName) str
                    { paramName = p.Name
                      paramType = looseType ty group }
                )
            let group, ty = extractTypeFromProto cmd.Proto
            let ret =
                { funcName = funcName 
                  parameters = parameters
                  retType = looseType ty group }
            ret
    )

let looslyTypedFunctionsToTypedFunctions enumMap functions =
    let typecheckParams parameters =
        let len = (parameters |> Array.length)
        let res = Array.zeroCreate len
        let rec typecheck index =
            if index < len then
                let currParam = parameters.[index]
                match currParam.paramType |> Parsing.tryParseType enumMap currParam.paramName with
                | Some ty ->
                    res.[index] <-
                        typedParameterInfo
                            currParam.paramName
                            ty
                    typecheck (index + 1)
                | None -> false
            else true
        if typecheck 0 then Some res
        else None

    functions
    |> Array.Parallel.choose(fun func ->
        maybe {
            let! retType = func.retType |> Parsing.tryParseType enumMap func.funcName
            let! parameters =
                func.parameters
                |> typecheckParams
            let res =
                typedFunctionDeclaration
                    func.funcName
                    parameters
                    retType
            return!
                Some res
        }
    )

let getEnumCasesAndCommandsPerVersion (data: OpenGL_Specification.Registry) =
    let featuresSortedAscendingByVersion = 
        data.Features
        |> Array.sortBy(fun data -> data.Number)
    let versions =
        featuresSortedAscendingByVersion
        |> Array.Parallel.map(fun data -> data.Number)
    let resultsByVersion = Array.zeroCreate versions.Length
    let aggregatedResultByVersion = Array.zeroCreate versions.Length
    let getVersionBefore index = aggregatedResultByVersion.[index - 1]

    let extractAddedAndRemoved(data: OpenGL_Specification.Feature) =
        let addedFunctions, addedEnums =
            data.Requires
            |> Array.Parallel.collectEither(fun e ->
                [| Left(e.Commands |> Array.Parallel.map(fun inner -> inner.Name))
                   Right(e.Enums |> Array.Parallel.map(fun e -> e.Name)) |])
        let removedFunctions, removedEnums =
            data.Removes
            |> Array.Parallel.collectEither(fun e ->
                [| Left(e.Commands |> Array.Parallel.map(fun inner -> inner.Name))
                   Right(e.Enums |> Array.Parallel.map(fun e -> e.Name)) |])
        
        {| addedFunctions = addedFunctions
           addedEnums = addedEnums
           removedFunctions = removedFunctions
           removedEnums = removedEnums |}

    let extractAddedAndRemovedAndSaveBack index =
        let data = featuresSortedAscendingByVersion.[index]
        let res = extractAddedAndRemoved data
        resultsByVersion.[index] <- res
        res

    let startIndex = 0
    let res = extractAddedAndRemovedAndSaveBack startIndex
    aggregatedResultByVersion.[startIndex] <-
        {| functions = res.addedFunctions
           enumCases = res.addedEnums |}

    let rec aggregateFunctions index =
        if index < featuresSortedAscendingByVersion.Length then
            let res = extractAddedAndRemovedAndSaveBack index
            let removedFunctionsSet = res.removedFunctions |> Set.ofArray
            let removedEnumCasesSet = res.removedEnums |> Set.ofArray
            let versionBefore = getVersionBefore index
            let functions =
                versionBefore.functions
                |> Array.Parallel.updateState
                    res.addedFunctions
                    removedFunctionsSet
            let enumCases =
                versionBefore.enumCases
                |> Array.Parallel.updateState
                    res.addedEnums
                    removedEnumCasesSet
            aggregatedResultByVersion.[index] <-
                {| functions = functions
                   enumCases = enumCases |}
            aggregateFunctions (index + 1)

    aggregateFunctions 1
    Array.Parallel.init (versions.Length) <| fun i ->
        let curr = aggregatedResultByVersion.[i]
        {| versionName = versions.[i]
           functions = curr.functions
           enumCases = curr.enumCases |}


[<EntryPoint>]
let main argv =
    printfn "Hello World from F//!"
    let path = @"../../../gl.xml"
    let test = OpenGL_Specification.Load path
    let enums = getEnumsFromSpecification test
    printfn "Enum group count: %d" enums.Length
    let enumMap =
        enums
        |> Array.Parallel.map(fun group ->
            group.groupName, group
        )
        |> Map.ofArray
    //enums
    //|> Seq.iter(fun enum ->
    //    printfn "-- enum group name: %A --" enum.groupName
    //    enum.cases
    //    |> Seq.iter(fun case ->
    //        printfn "case %A, value %A" case.name case.value
    //    )
    //    printfn "----"
    //)
    let functions = getFunctions test
    printfn "Function count: %d" functions.Length
    let possibleTypes =
        functions
        |> Array.Parallel.collect(fun func ->
            func.parameters
            |> Array.Parallel.map(fun p -> p.paramType)
        )
        |> Set.ofArray
    //for possibleType in possibleTypes do
    //    printfn "%A" possibleType
    let checkedTypes =
        possibleTypes
        |> Set.map(fun v -> v, Parsing.tryParseType enumMap "<unknown>" v)
    //for possibleType in checkedTypes do
    //    printfn "%A" possibleType
    let typecheckedFunctions = looslyTypedFunctionsToTypedFunctions enumMap functions
    printfn "overall correct function specifications: %d" typecheckedFunctions.Length
    let openGlSpecVersions = getEnumCasesAndCommandsPerVersion test
    for currOpenGL_Spec in openGlSpecVersions do
        generateEnums enums
        generateInterface typecheckedFunctions
        generateStaticClass typecheckedFunctions
        generateDummyTypes()
    //for func in typecheckedFunctions |> Array.take 100 do
    //    printfn "%A" func

    0 // return an integer exit code
