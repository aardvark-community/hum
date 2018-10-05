﻿namespace PointCloudViewer3

open System
open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.Geometry.Points

module Lod =
    
    type OctreeILodDataNode( globalCenter : V3d, node : PersistentRef<PointSetNode>, level : int ) =
        member x.Id = node.Value :> obj
        member x.Level = level
        member x.Bounds = node.Value.BoundingBox - globalCenter
        member x.LocalPointCount = node.Value.LodPointCount
        member x.Children = 
            if node.Value.IsNotLeaf then
                let sn = node.Value.Subnodes 
                let filtered = 
                    sn |> Array.choose ( fun node -> 
                        if node = null then
                            None
                        else
                            Some (OctreeILodDataNode(globalCenter,node,level+1) :> ILodDataNode)
                    )
                Some filtered
            else
                None

        interface ILodDataNode with
            member x.Id = x.Id
            member x.Level = level
            member x.Bounds = x.Bounds
            member x.LocalPointCount = x.LocalPointCount
            member x.Children = x.Children

    type OctreeLodData(ps : PointSet) =
        
        let globalCenter = ps.BoundingBox.Center

        //do
        //    let a = Array.zeroCreate 23905782
        //    let mutable i = 0
        //    ps.Root.Value.ForEachNode(true, (fun n -> 
        //            i <- i+1
        //            a.[i] <- n 
        //        ) 
        //    )

        let root = lazy ( OctreeILodDataNode(globalCenter, ps.Root,0) :> ILodDataNode )


        member x.BoundingBox = ps.BoundingBox

        member x.RootNode() =
            root.Value

        member x.Dependencies = []

        member x.GetData (node : ILodDataNode) : Async<Option<IndexedGeometry>> =
            async {
                let realNode = unbox<PointSetNode> node.Id
                let shiftGlobal = realNode.Center - globalCenter
                let pos = realNode.LodPositions.Value  |> Array.map(fun p -> V4f(V3f(shiftGlobal + (V3d p)),1.0f))
                let col = realNode.LodColors.Value  
                let r = 
                    IndexedGeometry(
                        Mode = IndexedGeometryMode.PointList,
                        IndexedAttributes =
                            SymDict.ofList [
                                DefaultSemantic.Positions, pos :> Array
                                DefaultSemantic.Colors, col :> Array
                            ]
                    )
                return Some r
                }

        interface ILodData with
            member x.BoundingBox = x.BoundingBox
            member x.RootNode() = x.RootNode()
            member x.Dependencies = x.Dependencies
            member x.GetData node = x.GetData node

