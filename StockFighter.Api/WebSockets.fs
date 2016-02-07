module StockFighter.Api.WebSockets

open System
open System.IO
open System.Text
open System.Threading
open System.Net.WebSockets

let encoder = new UTF8Encoding();
let receiveChunkSize = 1024

type ReceiveResult =
    | Success of string
    | Error of string

type ReceiveResultInternal =
    | Success of bool
    | Error of string
    | Close   

let parseResult (receiveResult:WebSocketReceiveResult) buffer (stringBuilder:StringBuilder) =
    match receiveResult.MessageType with
    | WebSocketMessageType.Close ->        
        Close
    | WebSocketMessageType.Binary ->
        Error "message was binary"
    | WebSocketMessageType.Text ->
        let str = encoder.GetString(buffer |> Array.filter (fun x -> x <> 0uy))
        stringBuilder.Append(str) |> ignore
        Success receiveResult.EndOfMessage

let receive (socket: ClientWebSocket) = async {    
    let stringBuilder = new StringBuilder()

    let rec receiveMore prevResult = async {
        let buffer = Array.create<byte> receiveChunkSize 0uy
        let! receiveResult = socket.ReceiveAsync(ArraySegment<byte>(buffer),CancellationToken.None) |> Async.AwaitTask

        match prevResult with
        | Success true ->
            return ReceiveResult.Success <| stringBuilder.ToString()
        | Success false ->
            let result = parseResult receiveResult buffer stringBuilder
            return! receiveMore result
        | Error str ->
            return ReceiveResult.Error str
        | Close ->
            do! socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None) |> Async.AwaitTask
            return ReceiveResult.Error "Socket closed by host"
    }
    
    return! receiveMore (Success true)
}

let startSocket wsUri postMsg = async {
    let socket = new ClientWebSocket()
    do! socket.ConnectAsync(wsUri, CancellationToken.None) |> Async.AwaitTask

    let mutable keepGoing = true

    while keepGoing do
        let! result = receive socket
        match result with
        | ReceiveResult.Success str -> postMsg str
        | ReceiveResult.Error str -> printfn "WebSocket error: %s" str; keepGoing <- false
}
