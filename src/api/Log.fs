module TelegramIndex.Log

open System
open FSharp.Control.Tasks.V2.ContextInsensitive
open System.Diagnostics
open FnArgs

open MongoDB.Bson
open MongoDB.Bson.Serialization.Attributes
open MongoDB.Driver
open MongoDB.Driver.Linq

open LogModel


type LogRecordBatch() =
    [<BsonId>]
    member val Id: ObjectId = ObjectId.Empty with get, set
    member val Timestamp: DateTimeOffset = DateTimeOffset.MinValue with get, set
    member val Records: System.Collections.Generic.List<BsonDocument> = null with get, set

type Interface = {
    LogCollection: IMongoCollection<LogRecordBatch>
}

let init (db: IMongoDatabase) =
    { LogCollection = db.GetCollection<LogRecordBatch>("log")}


let readAll iface = task {
    let! rs = iface.LogCollection.AsQueryable().OrderBy(fun x -> x.Timestamp).ToListAsync()
    return
        rs
        |> Seq.collect (fun x -> x.Records)
        |> Seq.map MongoPickler.unpickle<LogRecord>
}

let insert (logRecords: LogRecord list) iface = task {
    if logRecords.Length > 0 then
        let rs = logRecords |> List.map MongoPickler.pickle
        let batch =
            LogRecordBatch(
                Timestamp = DateTimeOffset.UtcNow,
                Records = new System.Collections.Generic.List<BsonDocument>(rs)
            )
        do! iface.LogCollection.InsertOneAsync(batch)
}

let reportException (exc: Exception) iface = task {
    try
        let err = exc.ToStringDemystified()
        do ConsoleLog.print <| err
        do! err |> LogRecord.Exception |> List.singleton |> (flip insert iface)
    with e ->
        do ConsoleLog.print "can not report an exception to the DB"
        do ConsoleLog.print <| exc.ToString()

    return ()
}

let trackTask (trackableTask: Task.TplUnitTask) iface = ignore <| task {
    try
        do! trackableTask
        return ()
    with e ->
        do! reportException e iface
}
