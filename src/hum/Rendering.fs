﻿(*
    Copyright (c) 2018. Attila Szabo, Georg Haaser, Harald Steinlechner, Stefan Maierhofer.
    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.
    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*)
namespace hum

open System
open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.Geometry.Points
open Aardvark.Base.Incremental

module Lod =
    open hum.Model
    open System.Threading
    
    type OctreeILodDataNode(globalCenter : V3d, node : PersistentRef<IPointCloudNode>, level : int ) =
        member x.Identifier = node.Value.Cell.ToString()
        member x.Node = node.Value :> obj
        member x.Level = level
        member x.Bounds = node.Value.Cell.BoundingBox - globalCenter
        member x.LocalPointCount = if node.Value.HasLodPositions() then int64 (node.Value.GetLodPositions().Value.Length) else 0L
        member x.Children = 
            if node.Value.IsNotLeaf() then
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

        override x.GetHashCode() = x.Identifier.GetHashCode()
        override x.Equals o =
            match o with
                | :? OctreeILodDataNode as o -> x.Identifier = o.Identifier
                | _ -> false

        interface ILodDataNode with
            member x.Id = x.Node
            member x.Level = level
            member x.Bounds = x.Bounds
            member x.LocalPointCount = x.LocalPointCount
            member x.Children = x.Children

    type OctreeLodData(ps : PointSet) =
        
        let globalCenter = ps.BoundingBox.Center
        
        let rv = ps.Root.Value :> IPointCloudNode
        let f = Func<string, CancellationToken, IPointCloudNode>(fun _ _ -> rv)
        let r = PersistentRef<IPointCloudNode>("whatever", f)
        let root = lazy ( OctreeILodDataNode(globalCenter, r, 0) :> ILodDataNode )
        
        member x.BoundingBox = ps.BoundingBox

        member x.RootNode() = root.Value

        member x.Dependencies = []

        member x.GetData (node : ILodDataNode) : Async<Option<IndexedGeometry>> =
            async {
                let realNode = unbox<IPointCloudNode> node.Id
                let shiftGlobal = realNode.Center - globalCenter
                
                let pos = realNode.GetLodPositions().Value  |> Array.map(fun p -> V4f(V3f(shiftGlobal + (V3d p)), 1.0f))
                
                let normals = 
                    match realNode.HasLodNormals() with
                    | true -> realNode.GetLodNormals().Value |> Array.map(fun p -> V3f(p))
                    | false -> realNode.GetLodPositions().Value  |> Array.map(fun _ -> V3f.III)

                let labels =
                    match realNode.HasLodClassifications() with
                    | true -> realNode.GetLodClassifications().Value |> Array.map (fun c -> int(c))
                    | false -> realNode.GetLodPositions().Value  |> Array.map(fun _ -> 0)
        
                let colors = 
                    match realNode.HasLodColors() with
                    | true -> realNode.GetLodColors().Value
                    | false -> realNode.GetLodPositions().Value  |> Array.map(fun _ -> C4b.White)
                    
                //let col = match realNode.HasLodColors, realNode.HasLodClassifications with
                //          | true , _ -> realNode.LodColors.Value
                //          | _    , true  -> realNode.LodClassifications.Value |> Array.map (fun c -> colorScheme.[int(c)])
                //          | false, false -> realNode.LodPositions.Value |> Array.map (fun _ -> C4b.Gray)
                
                let r = 
                    IndexedGeometry(
                        Mode = IndexedGeometryMode.PointList, 
                        IndexedAttributes =
                            SymDict.ofList [
                                DefaultSemantic.Positions, pos :> Array
                                DefaultSemantic.Normals, normals :> Array
                                DefaultSemantic.Colors, colors :> Array
                                DefaultSemantic.Label, labels :> Array
                            ]
                    )
                return Some r
                }

        interface ILodData with
            member x.BoundingBox = x.BoundingBox
            member x.RootNode() = x.RootNode()
            member x.Dependencies = x.Dependencies
            member x.GetData node = x.GetData node

