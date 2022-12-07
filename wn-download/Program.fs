open System
open System.IO
open System.Threading
open OpenQA.Selenium.Chrome
open WebDriverManager
open WebDriverManager.DriverConfigs.Impl
open WebDriverManager.Helpers

writeLog Info $"{Environment.NewLine}=== 開始します ==="

(* ChromeDriver のセットアップ *)

DriverManager()
    .SetUpDriver(ChromeConfig(), VersionResolveStrategy.MatchingBrowser)
|> ignore

let driver = new ChromeDriver()

let mailAddressFilename = "mailAddress.txt"
let passwordFilename = "password.txt"

(* 実行ファイルのディレクトリを設定 *)
if Directory.GetCurrentDirectory()
   <> AppContext.BaseDirectory then
    Directory.SetCurrentDirectory AppContext.BaseDirectory
else
    ()

exception Error of string

let mailAddress =
    try
        if File.Exists mailAddressFilename then
            let text = File.ReadAllText mailAddressFilename

            writeLog Success "メールアドレスをファイルから読み取りました"
            text.Trim()
        else
            failwith $"メールアドレスのファイル {mailAddressFilename} が見つかりません、、、"
    with
    | Failure (msg) ->
        printfn $"%s{msg}"
        driver.Quit()
        Environment.Exit 0
        ""

let password =
    try
        if File.Exists passwordFilename then
            let text = File.ReadAllText passwordFilename
            writeLog Success "パスワードをファイルから読み取りました"
            text.Trim()
        else
            failwith $"パスワードのファイル {passwordFilename} が見つかりません、、、"
    with
    | Failure (msg) ->
        printfn $"%s{msg}"
        driver.Quit()
        Environment.Exit 0
        ""


login driver mailAddress password
Thread.Sleep 4000

let lastDoneFilename = "wn-download-last-done.txt"

let lastDoneFilePath = Path.Combine(AppContext.BaseDirectory, lastDoneFilename)

let lastDoneIndex =
    if File.Exists lastDoneFilePath then
        let str = File.ReadAllText(lastDoneFilePath)
        let msg1 = $"%s{lastDoneFilename} を読み込みました"
        writeLog Info msg1
        printfn $"%s{msg1}"

        match Int32.TryParse str with
        | true, number ->
            let msg1 = $"data-index: {number} までは処理をスキップします"

            writeLog Info msg1
            printfn $"%s{msg1}"
            number

        | _, _ ->
            let msg2 = $"正しい数値（半角数字）ではないようです、、、最初から処理を開始します"

            writeLog Info msg2
            printfn $"%s{msg2}"
            -1
    else
        let msg1 = "最初（data-index: 0）から処理を開始します"
        writeLog Info msg1
        printfn $"%s{msg1}"
        -1


// last の数字が増えるまではスクロール
// last の数字が増えたらまた first-last を処理（済は飛ばす）
// -> 処理済み index を保持しておく
let scrollAndDownload (driver: ChromeDriver) lastDoneIndex =
    let scrollDown () =
        scroll driver 200
        Thread.Sleep 500

    let scrollDownFast () =
        scroll driver 200
        Thread.Sleep 250

    let rec loop (lastIndex: int) history =
        (* data-index を受け取り、処理済みなら飛ばす *)
        let downloadIfNotYet i =
            if i <= lastIndex then
                ()
            else
                download driver i

        let addedHistory = lastIndex :: history
        let arr = Array.ofList addedHistory

        (* 10 回スクロールしても last が同じなら停止する *)
        if (List.length addedHistory) > 10 && arr[0] = arr[9] then
            ()
        else
            match getFirstAndLastIndices driver with
            | first, last ->
                printfn $"{string first}, {string last} : {addedHistory}"

                if last < lastDoneIndex then
                    scrollDownFast ()
                    printfn "（スキップ）"
                    loop last addedHistory
                else
                    if last > lastIndex then
                        [ first..last ] |> List.iter downloadIfNotYet
                    else
                        ()

                    scrollDown ()
                    loop last addedHistory

    loop -1 []

scrollAndDownload driver lastDoneIndex

writeLog Info "=== 終了します ==="
driver.Quit()
