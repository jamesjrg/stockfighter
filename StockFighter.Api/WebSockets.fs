module StockFighter.Api.WebSockets

open System
open System.IO
open System.Text
open System.Threading
open System.Net.WebSockets

let private encoder = new UTF8Encoding();
let private receiveChunkSize = 1024
let private resetTimeout = 20000

type ReceiveResult =
    | Success of string
    | Error of string

type private ReceiveResultInternal =
    | Success of bool
    | Error of string
    | Close   

let private parseResult (receiveResult:WebSocketReceiveResult) buffer (stringBuilder:StringBuilder) =
    match receiveResult.MessageType with
    | WebSocketMessageType.Close ->        
        Close
    | WebSocketMessageType.Binary ->
        Error "message was binary"
    | WebSocketMessageType.Text ->
        let str = encoder.GetString(buffer |> Array.filter (fun x -> x <> 0uy))
        stringBuilder.Append(str) |> ignore
        match str.Length with
        | 0 -> Error "0 length string"
        | _ -> Success receiveResult.EndOfMessage
    | _ -> Error "this should be impossible"

let withTimeout (operation: Async<'x>) (timeOut: int) = async {
    let! child = Async.StartChild (operation, timeOut)
    try
        let! result = child
        return Some result
    with :? System.TimeoutException ->
        return None
}

let private receive (socket: ClientWebSocket) = async {    
    let stringBuilder = new StringBuilder()

    let rec receiveMore prevResult = async {        
        match prevResult with
        | Success true ->
            return ReceiveResult.Success <| stringBuilder.ToString()
        | Success false ->
            let buffer = Array.create<byte> receiveChunkSize 0uy
            let! resultOrTimeout =
                withTimeout
                    (socket.ReceiveAsync(ArraySegment<byte>(buffer), CancellationToken.None) |> Async.AwaitTask)
                    resetTimeout

            match resultOrTimeout with
            | Some result ->
                let result = parseResult result buffer stringBuilder
                return! receiveMore result
            | None -> return ReceiveResult.Error "timeout"
        | Error str ->
            return ReceiveResult.Error str
        | Close ->
            do! socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None) |> Async.AwaitTask
            return ReceiveResult.Error "Socket closed by host"
    }
    
    return! receiveMore (Success false)
}

let startSocket logger wsUri postMsg = async {
    let socket = new ClientWebSocket()
    logger (sprintf "Connecting to %O" wsUri)
    do! socket.ConnectAsync(wsUri, CancellationToken.None) |> Async.AwaitTask
    logger (sprintf "Connected")
    let mutable keepGoing = true

    while keepGoing do
        let! result = receive socket
        match result with
        | ReceiveResult.Success str ->
            postMsg result
        | ReceiveResult.Error str ->
            postMsg result
            logger (sprintf "Reconnecting to %O" wsUri)
            do! socket.ConnectAsync(wsUri, CancellationToken.None) |> Async.AwaitTask
            logger "Reconnected"
}
