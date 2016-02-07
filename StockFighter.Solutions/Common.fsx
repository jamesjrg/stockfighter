#I __SOURCE_DIRECTORY__
#r "../packages/Http.fs-prerelease/lib/net40/HttpFs.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

#load "../StockFighter.API/WebSockets.fs"
#load "../StockFighter.API/DtoTypes.fs"
#load "../StockFighter.API/Settings.fs"
#load "../StockFighter.API/DomainTypes.fs"
#load "../StockFighter.API/Functions.fs"

#r "System.Core.dll"
#r "System.dll"
#r "System.Numerics.dll"

open System
open StockFighter.Api
open StockFighter.Api.Settings
open StockFighter.Api.Stocks

let apiKeyFile = @"C:\Users\james\Dropbox\dev\stockfighter\apikey.txt"
let instanceSettingsFile = @"instance_settings.txt" //FSI in Visual Studio will put this in the user's temp folder
let apiKey = IO.File.ReadAllText(apiKeyFile)

let startLevel level = async {
    let! response = GamesMaster.startLevel apiKey level
    let settings = createSettings apiKey response
    IO.File.WriteAllText(instanceSettingsFile, (serializeSettings settings));
    return settings
}

let restart = async {
    let settings = deserializeSettings(IO.File.ReadAllText(instanceSettingsFile))
    let! result = GamesMaster.restartInstance apiKey settings.InstanceId
    printfn "%s" result
    return settings
}

//for less typing in fsi
let sync f =
    f |> Async.RunSynchronously



