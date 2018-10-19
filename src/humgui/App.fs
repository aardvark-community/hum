namespace humgui

open System
open System.IO
open System.Threading
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Base.Rendering
open humgui.Model

// remove
open Aardvark.Geometry.Points
open Aardvark.Data.Points
open Aardvark.Data.Points.Import
open Uncodium.SimpleStore

open hum

type Message =
    | CameraMessage     of FreeFlyController.Message
    | OpenStore         of list<string>
    | LoadKeyFromStore  of string
    | ImportFiles       of list<string>
    | ShowFileInfos     of list<string>
    | ToggleLod
    | Nop

module Store =
    let tryOpenStore (dirname : string) =
        try
            if (Directory.Exists(dirname)) then
                Result.Ok (PointCloud.OpenStore dirname)
            else
                Result.Error (sprintf "store '%s' does not exist." dirname)
        with e -> 
            Result.Error e.Message

    let tryOpenPointSet (m : Model) (key : string) =
        Log.line "tryOpenPointSet %s" key
        match m.store with
            | None -> Result.Error "no store"
            | Some store -> 
                try
                    match store.GetPointSet(key, CancellationToken.None) with
                        | null -> Result.Error (sprintf "could not load key: %s" key)
                        | v -> Result.Ok v
                with e -> 
                    Result.Error e.Message

module App =

    let initial = {  
        cameraState = FreeFlyController.initial
        pointSet = None
        store = None
        createLod = true
    }

    let update (m : Model) (msg : Message) =
        match msg with
            | CameraMessage msg ->
                { m with cameraState = FreeFlyController.update m.cameraState msg }
                
            | OpenStore paths -> 
                match paths with
                    | path::[] -> 
                        match Store.tryOpenStore path with
                            | Result.Ok newStore -> 
                                //m.store |> Option.iter (fun store -> store.Dispose())
                                Log.line "loaded new store: %A" paths
                                { m with store = Some newStore; pointSet = None }
                            | Result.Error err -> 
                                // to ui log
                                Log.error "%s" err
                                m
                    | _ -> m
                     
            | LoadKeyFromStore id -> 
                match Store.tryOpenPointSet m id with
                    | Result.Ok v -> Log.line "loaded id: %s, splitLimit: %d" id v.SplitLimit; { m with pointSet = Some v }
                    | Result.Error str -> Log.error "%s" str; m

            | ImportFiles filenames ->
                Log.line "[ImportFiles] not implemented"
                m

            | ShowFileInfos filenames ->
                Log.line "[ShowFileInfos] not implemented"
                m

            | ToggleLod ->
                Log.line "toggling lod"
                { m with createLod = not m.createLod }
                     
            | Nop -> m
                

    let dependencies = [] @ Html.semui

    // https://semantic-ui.com

    let adornerMenu (sectionsAndItems : list<string * list<DomNode<'msg>>>) (rest : list<DomNode<'msg>>) =
        let pushButton() = 
            div [
                clazz "ui black big launch right attached fixed button menubutton"
                js "onclick"        "$('.sidebar').sidebar('toggle');"
                style "z-index:1"
            ] [
                i [clazz "content icon"] [] 
                span [clazz "text"] [text "Menu"]
            ]
        [
            yield 
                div [clazz "pusher"] [
                    yield pushButton()                    
                    yield! rest                    
                ]
            yield 
                Html.SemUi.menu "ui vertical inverted sidebar menu" sectionsAndItems
        ]
        
    let toggleBox (msg : string) (state : IMod<bool>) (toggle : unit -> 'msg) =

        let attributes = 
            amap {
                yield attribute "type" "checkbox"
                yield onChange (fun _ -> toggle ())
                let! check = state
                if check then
                    yield attribute "checked" "checked"
            }

        onBoot "$('#__ID__').checkbox()" (
            div [clazz "ui checkbox"] [
                Incremental.input (AttributeMap.ofAMap attributes)
                label [style "color:white"] [text msg] 
            ]
        )
        
    let onChooseFiles (chosen : list<string> -> 'msg) =
        let cb xs =
            match xs with
                | [] -> chosen []
                | x::[] when x <> null -> x |> Aardvark.Service.Pickler.json.UnPickleOfString |> List.map Aardvark.Service.PathUtils.ofUnixStyle |> chosen
                | _ -> failwithf "onChooseFiles: %A" xs
        onEvent "onchoose" [] cb

    let view (m : MModel) =

        let frustum = 
            Frustum.perspective 60.0 1.0 50000.0 1.0 |> Mod.constant
            
        let sg = 
            adaptive {
                let! ps = m.pointSet
                match ps with
                    | None -> return Sg.empty 
                    | Some ps -> 
                        return Scene.createSceneGraph ps [||] |> Sg.noEvents
            } |> Sg.dynamic

        let att =
            [
                style "position: fixed; left: 0; top: 0; width: 100%; height: 100%"
            ]

        let mainView = 
            FreeFlyController.controlledControl m.cameraState CameraMessage frustum (AttributeMap.ofList att) sg

        let main =
             adornerMenu [
                "View", [
                    
                    button [
                        onChooseFiles OpenStore
                        clientEvent "onclick" ("aardvark.processEvent('__ID__', 'onchoose', aardvark.dialog.showOpenDialog({properties: ['openFile', 'openDirectory']}));") 
                    ] [text "Open store"]

                    label [] [text "Pointcloud key:"]
                    input [onChange LoadKeyFromStore]

                    //div [clazz "ui labeled input"] [
                    //    div [clazz "ui label"] [text "Pointcloud key:"]
                    //    input ["type" "text"] []
                    //]
                ];
                "Import", [
                    button [
                        onChooseFiles ImportFiles
                        clientEvent "onclick" ("aardvark.processEvent('__ID__', 'onchoose', aardvark.dialog.showOpenDialog({properties: ['openFile', 'multiSelections']}));") 
                    ] [text "Import file"]
                    toggleBox "create levels-of-detail" m.createLod (fun () -> ToggleLod)
                ];
                "Info", [
                    button [
                        onChooseFiles ShowFileInfos
                        clientEvent "onclick" ("aardvark.processEvent('__ID__', 'onchoose', aardvark.dialog.showOpenDialog({properties: ['openFile', 'multiSelections']}));") 
                    ] [text "Show file info"]
                ]
             ] [mainView]

        require dependencies (
            body [] main
        )

    let app =
        {
            initial = initial
            update = update
            view = view
            threads = fun _ -> ThreadPool.empty
            unpersist = Unpersist.instance
        }