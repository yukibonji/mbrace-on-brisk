﻿module MBraceWorkerRole.WebHost

open MBrace.Azure.Client
open Newtonsoft.Json
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Successful
open Suave.Http.Writers
open Suave.Types
open Suave.Web

type ClusterStats =
    { ActiveJobs : int
      CpuAverage : double
      MemoryInUse : double
      MemoryAvailable : double
      MemoryTotal : double
      NetworkDown : double
      NetworkUp : double
      Nodes : int }

[<AutoOpen>]
module private Helpers =
    let private jsonHeader = setHeader "Content-Type" "application/json"
    let asJson body : WebPart = jsonHeader >>= OK (body |> JsonConvert.SerializeObject)
    let withCors = setHeader "Access-Control-Allow-Origin" "*"

[<AutoOpen>]
module private WebParts =
    let getStats (handle:Runtime) =
        let workers = handle.GetWorkers() |> Seq.toList
        match workers with
        | [] -> { ActiveJobs = 0; CpuAverage = 0.; MemoryInUse = 0.; MemoryAvailable = 0.; MemoryTotal = 0.; NetworkDown = 0.; NetworkUp = 0.; Nodes = 0 }
        | workers ->           
            let totalMemory = workers |> Seq.sumBy(fun worker -> worker.TotalMemory)
            let memoryInUse = workers |> Seq.sumBy(fun worker -> worker.Memory)
            { ActiveJobs = workers |> Seq.sumBy(fun worker -> worker.ActiveJobs)
              CpuAverage = workers |> Seq.averageBy(fun worker -> worker.CPU)
              Nodes = workers.Length
              MemoryInUse = memoryInUse
              MemoryAvailable = totalMemory - memoryInUse
              MemoryTotal = totalMemory
              NetworkDown = workers |> Seq.sumBy(fun worker -> worker.NetworkDown)
              NetworkUp = workers |> Seq.sumBy(fun worker -> worker.NetworkUp) }

let private createApp cluster =
    choose
        [ OPTIONS >>= OK "OK" >>= withCors
          GET >>= choose
            [ path "/stats" >>= context(fun _ -> (getStats cluster |> asJson)) >>= withCors ] ]

/// Starts hosting the MBrace web host for exposing stats.
let startHosting mbraceConfig (ip, port) =
    let mbraceHandle = Runtime.GetHandle mbraceConfig
    
    let webConfig =
        { defaultConfig with
            bindings = [ { HttpBinding.defaults with
                            socketBinding = { ip = ip; port = uint16 port } } ] }

    startWebServer webConfig (createApp mbraceHandle)
