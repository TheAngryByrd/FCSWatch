module FcsWatch.InteractionTests.InteractionTests
open Expecto
open Microsoft.FSharp.Compiler.SourceCodeServices
open System.IO
open FcsWatch
open Fake.IO
open Fake.IO.FileSystemOperators
open System.Threading
open FcsWatch.Types
open FcsWatch.FcsWatcher
open FcsWatch.CompilerTmpEmiiter
open Fake.DotNet

let pass() = Expect.isTrue true "passed"
let fail() = Expect.isTrue false "failed"

let root = __SOURCE_DIRECTORY__ </> "../../"

let datas = Path.getDirectory(__SOURCE_DIRECTORY__) </> "datas"

let entryProjDir = datas </> "TestProject"

let entryProjPath  = entryProjDir </> "TestProject.fsproj"

let testProjPath = datas </> @"TestLib2/TestLib2.fsproj"

let testSourceFile1 = datas </> @"TestLib2/Library.fs"

let testSourceFile2 = datas </> @"TestLib2/Library2.fs"

let testSourceFileAdded = datas </> @"TestLib2/Added.fs"

let testSourceFile1InTestLib = datas </> @"TestLib1/Library.fs"

let makeFileChange fullPath : FileChange =
    let fullPath = Path.getFullName fullPath

    { FullPath = fullPath
      Name = Path.GetFileName fullPath
      Status = FileStatus.Changed }

let makeFileChanges fullPaths =
    fullPaths |> List.map makeFileChange |> FcsWatcherMsg.DetectSourceFileChanges


let createWatcher buildingConfig =

    let checker = FSharpChecker.Create()

    let fcsWatcher =
        fcsWatcher buildingConfig checker entryProjPath

    let testData = createTestData()
    /// consume warm compile testData
    testData.SourceFileManualSet.Wait()

    fcsWatcher


DotNet.build id entryProjDir


let interactionTests =

    testCase "base interaction test" <| fun _ ->
        let manualSet = new ManualResetEventSlim()
        let watcher = createWatcher (fun config -> {config with WorkingDir = root; LoggerLevel = Logger.Level.Normal } )
        manualSet.Wait()