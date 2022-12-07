[<AutoOpen>]
module Wellnote

open System
open OpenQA.Selenium
open OpenQA.Selenium.Chrome

type Comment =
    { Author: string
      DateTime: DateTime
      Text: string option
      Sticker: string option }

type PostContents =
    { Author: string
      DateTime: DateTime
      Text: string option
      PhotosOrVideo: string list option
      Comments: Comment list option }

let byc = By.CssSelector

// ログインページを開く（開くだけでログインはしない）
let openLoginPage (driver: ChromeDriver) =
    let loginUrl = "https://wellnote.jp/login"
    driver.Navigate().GoToUrl loginUrl

// ログインする
let login (driver: ChromeDriver) mailAddress password =
    writeLog Attempt "ログインします"
    let loginUrl = "https://wellnote.jp/login"
    driver.Navigate().GoToUrl loginUrl

    driver
        .FindElement(By.Id "loginId")
        .SendKeys mailAddress

    driver
        .FindElement(By.Id "password")
        .SendKeys password

    driver.FindElement(byc "button.sc-pVTFL").Click()
    writeLog Success "ログインしました"

// スクロールさせる
let scroll (driver: ChromeDriver) x =
    driver.ExecuteScript($"window.scrollBy(0, %d{x});")
    |> ignore

// 現在の表示領域における最初の data-index を取得する
let dataIndexOfFirst (driver: ChromeDriver) =
    let divs = driver.FindElements(byc "div[data-index]")

    divs[ 0 ].GetAttribute "data-index" |> int

// 現在の表示領域における最後の data-index を取得する
let dataIndexOfLast (driver: ChromeDriver) =
    let divs = driver.FindElements(byc "div[data-index]")

    divs[ divs.Count - 1 ].GetAttribute "data-index"
    |> int

// 現在の表示領域における最初と最後の data-index を tuple で取得する
let getFirstAndLastIndices (driver: ChromeDriver) =
    dataIndexOfFirst driver, dataIndexOfLast driver


(* modal の操作 *)

open OpenQA.Selenium.Interactions

// modal を表示させる操作
let openModal (driver: ChromeDriver) =
    let image = driver.FindElement(byc "div.sc-haTkiu")

    image.Click()

// modal が表示されているか判定する
let isModalOpened (driver: ChromeDriver) =
    let childDiv = driver.FindElements(byc "div#modal-root > div")

    childDiv.Count > 0

// modal を閉じる操作
let closeModal (driver: ChromeDriver) =
    writeLog Attempt "モーダルウィンドウを閉じます"
    let actions = new Actions(driver)

    actions
        .KeyDown(Keys.Escape)
        .KeyUp(Keys.Escape)
        .Perform()

    if isModalOpened driver then
        writeLog Success "モーダルウィンドウを閉じました"
    else
        writeLog Fail "モーダルウィンドウを閉じることに失敗しました"


(* 各投稿に対する処理 *)

open System.IO

// 各投稿を data-index を指定して取得する
let getPost (driver: ChromeDriver) (index: int) =
    driver.FindElement(byc $"div[data-index=\"{string index}\"]")

// テキストを取得する
let tryGetText (post: IWebElement) =
    let text = post.FindElements(byc "p.sc-hUplSX")

    if text.Count = 0 then
        None
    else
        Some(text[0].Text)

// 画像・動画の有無を確認してダウンロードする
let tryGetPhotosOrVideo (driver: ChromeDriver) (post: IWebElement) (prefix: string) =
    let image = post.FindElements(byc "div.sc-haTkiu")

    if image.Count > 0 then
        // modal を表示させる
        image[ 0 ].Click()

        let modalDiv = driver.FindElement(byc "div#modal-root")

        let innerDiv = modalDiv.FindElement(byc "div.swiper-wrapper")

        try
            // 動画か写真かを判定する
            if innerDiv.FindElements(byc "video").Count > 0 then
                // 動画ファイルをダウンロードする
                writeLog Info "動画ファイルがあります"

                let video = innerDiv.FindElement(byc "video")

                let videoUrl = video.GetAttribute "src"
                downloadFileWithPrefix prefix videoUrl

                let filename = Array.last (Uri(videoUrl).Segments)

                Some([ $"%s{prefix}_%s{filename}" ])
            else
                // 画像ファイルをダウンロードする
                let images = innerDiv.FindElements(byc "div.swiper-slide > img")

                writeLog Info $"画像ファイルが %d{Seq.length images} 点あります"

                let imageUrls =
                    images
                    |> Seq.map (fun x -> x.GetAttribute "src")
                    |> List.ofSeq

                List.iter (downloadFileWithPrefix prefix) imageUrls

                let imageFilenames =
                    List.map
                        (fun url ->
                            let filename = Array.last (Uri(url).Segments)

                            $"%s{prefix}_%s{filename}.jpg")
                        imageUrls

                Some(imageFilenames)
        finally
            closeModal driver
    else
        None

// 各コメント投稿をパースする
let parseComment (article: IWebElement) =
    let author = article.FindElement(byc "div.sc-kjOQFR").Text

    let datetime =
        article
            .FindElement(byc "div.sc-jnrVZQ > time")
            .GetAttribute "datetime"
        |> DateTime.Parse

    let body = article.FindElement(byc "div.sc-eSxRXt")

    let p = body.FindElements(byc "p")

    let text =
        if p.Count > 0 then
            Some p[0].Text
        else
            None

    let img = body.FindElements(byc "img")

    let stickerFilename =
        if img.Count > 0 then
            let url = img[ 0 ].GetAttribute "src"

            let filename = Array.last (Uri(url).Segments)

            downloadFile url filename
            Some(filename)
        else
            None

    { Author = author
      DateTime = datetime
      Text = text
      Sticker = stickerFilename }

// コメントを取得する
let tryGetComments (post: IWebElement) : Comment list option =
    let comments =
        post
            .FindElement(byc "section.sc-hAWBJg")
            .FindElements(byc "article.sc-cqJhZP")

    if comments.Count > 0 then
        writeLog Info $"コメントが %d{Seq.length comments} 件あります"

        comments
        |> List.ofSeq
        |> List.map parseComment
        |> Some
    else
        None

open System.Text.Json
open System.Text.Encodings.Web
open System.Text.Unicode

let options =
    new JsonSerializerOptions(
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    )

// 最後に処理した data-index を記録しておく
let lastDoneFilename = "wn-download-last-done.txt"
let lastDoneFilePath = Path.Combine(AppContext.BaseDirectory, lastDoneFilename)

// data-index を指定して投稿のコンテンツをダウンロードする
let download (driver: ChromeDriver) (index: int) =
    let post = getPost driver index

    let author = post.FindElement(byc "div.sc-ciFQTS").Text

    let datetime =
        post
            .FindElement(byc "div.sc-kJpAUB > time")
            .GetAttribute "datetime"
        |> DateTime.Parse

    let msg1 = $"[data-index: %d{index}] {datetime.ToString()} のダウンロードを開始します"
    printfn $"{msg1}"
    writeLog Attempt msg1

    // 年/月/日のディレクトリに移動する
    cdToDownloadDirectory datetime

    let prefix = datetime.ToString "yyyy-MM-dd-HHmmss"

    // Text の有無を確認する
    let text = tryGetText post

    // 写真、動画の有無を確認する
    let photosOrVideo = tryGetPhotosOrVideo driver post prefix

    // コメントの有無を確認する
    let comments = tryGetComments post

    let postContents =
        { Author = author
          DateTime = datetime
          Text = text
          PhotosOrVideo = photosOrVideo
          Comments = comments }

    let json = JsonSerializer.Serialize<PostContents>(postContents, options)

    File.WriteAllText($"{prefix}_postContents.json", json)

    // カレントディレクトリを戻しておく
    Directory.SetCurrentDirectory AppContext.BaseDirectory

    // 処理した data-index をファイルに記録しておく
    File.WriteAllText(lastDoneFilePath, string index)

    let msg2 = $"[data-index: %d{index}] {datetime.ToString()} のダウンロードが完了しました"

    printfn $"{msg2}"
    writeLog Success msg2
