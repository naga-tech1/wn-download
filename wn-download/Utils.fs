[<AutoOpen>]
module Utils

open System
open System.IO
open System.Net.Http
open System.Net
open System.Threading

(* shorthand *)
let run = Async.RunSynchronously

let await = fun x -> Async.AwaitTask x |> run


(* ログをファイルに書き込む *)
open System.Text

let logFileBasename = "wn-download-log.txt"

let logFilePath = Path.Combine(AppContext.BaseDirectory, logFileBasename)

type LogLevel =
    | Info
    | Attempt
    | Success
    | Fail
    | Error

let writeLog_ (logLevel: LogLevel) logContent = ()

let inline writeLog (logLevel: LogLevel) logContent =
    let dt = DateTime.Now.ToString("MM-dd HH:mm:ss")

    let level =
        match logLevel with
        | Info -> "Info"
        | Attempt -> "Attempt"
        | Success -> "Success"
        | Fail -> "Fail"
        | Error -> "Error"

    let logFormat = $"%s{dt} %s{level} : %s{logContent}{Environment.NewLine}"

    File.AppendAllText(logFilePath, logFormat, Encoding.UTF8)
    printfn $"{logFormat}"


(* HTTP request *)
let httpClient = new HttpClient()

httpClient.DefaultRequestHeaders.Add(
    "User-Agent",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.0.0 Safari/537.36"
)

// ファイルをダウンロードする
let inline downloadFile fileUrl saveFilename =
    if File.Exists saveFilename then
        ()
    else
        let msg1 = $"ファイル %s{fileUrl} をダウンロードします"
        writeLog Attempt msg1

        let request = new HttpRequestMessage(HttpMethod.Get, Uri fileUrl)

        let response =
            httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            |> await

        Thread.Sleep(1000)

        match response.StatusCode with
        | HttpStatusCode.OK ->
            let cs = response.Content.ReadAsStreamAsync() |> await

            let fs =
                new FileStream(saveFilename, FileMode.Create, FileAccess.Write, FileShare.None)

            cs.CopyTo fs
            let msg2 = $"ダウンロードが成功しました"
            writeLog Success msg2

        | _ ->
            let msg3 = $"ダウンロードが失敗しました StatusCode: {response.StatusCode} URL: %s{fileUrl}"

            writeLog Fail msg3
            printfn $"%s{msg3}"

// 指定したプレフィクスをつけたファイル名でダウンロードする
let downloadFileWithPrefix prefix fileUrl =
    let urlFilename = Array.last (Uri(fileUrl).Segments)

    let saveFilename =
        if Path.HasExtension urlFilename then
            $"%s{prefix}_{urlFilename}"
        else
            (* 拡張子がないものは写真と判断する *)
            $"%s{prefix}_{urlFilename}.jpg"

    downloadFile fileUrl saveFilename


(* ディレクトリ作成、移動 *)

// ディレクトリがなければ作成し、移動する
let inline createDirAndCd dirName =
    if Directory.Exists dirName then
        ()
    else
        Directory.CreateDirectory dirName |> ignore

    Directory.SetCurrentDirectory dirName

let rootDirName = "wellnote-download"

// 年/月/日 でディレクトリを作成する
let cdToDownloadDirectory (dt: DateTime) =
    Directory.SetCurrentDirectory AppContext.BaseDirectory
    createDirAndCd rootDirName
    createDirAndCd (string dt.Year)
    createDirAndCd (string dt.Month)
    createDirAndCd (string dt.Day)

    let msg1 = $"カレントディレクトリを {dt.Year}/{dt.Month}/{dt.Day} に変更しました"

    writeLog Info msg1
