﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TheHypercube;
using UnityEngine;

using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of The Hypercube
/// Created by Timwi
/// </summary>
public class TheHypercubeModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMRuleSeedable RuleSeedable;
    public KMAudio Audio;
    public Transform Hypercube;
    public Transform[] Edges;
    public KMSelectable[] Vertices;
    public MeshFilter[] Faces;
    public Mesh Quad;
    public Material FaceMaterial;

    // Rule-seed
    private int[][] _colorPermutations;
    private List<bool?[]> _faces;

    private static int _moduleIdCounter = 1;
    private int _moduleId;

    private int[] _rotations;
    private float _hue, _sat, _v;
    private Coroutine _rotationCoroutine;
    private bool _transitioning;
    private int _progress;
    private int[] _vertexColors;
    private int _correctVertex;

    private Material _edgesMat, _verticesMat, _facesMat;
    private List<Mesh> _generatedMeshes = new List<Mesh>();
    private static readonly string[] _rotationNames = new[] { "XY", "YX", "XZ", "ZX", "XW", "WX", "YZ", "ZY", "YW", "WY", "ZW", "WZ" };
    private static readonly string[][] _dimensionNames = new[] { new[] { "left", "right" }, new[] { "bottom", "top" }, new[] { "front", "back" }, new[] { "zig", "zag" } };
    private static readonly string[] _colorNames = new[] { "red", "yellow", "green", "blue" };
    private static readonly Color[] _vertexColorValues = "e54747,e5e347,47e547,3ba0f1".Split(',').Select(str => new Color(Convert.ToInt32(str.Substring(0, 2), 16) / 255f, Convert.ToInt32(str.Substring(2, 2), 16) / 255f, Convert.ToInt32(str.Substring(4, 2), 16) / 255f)).ToArray();

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        _edgesMat = Edges[0].GetComponent<MeshRenderer>().material;
        for (int i = 0; i < Edges.Length; i++)
            Edges[i].GetComponent<MeshRenderer>().sharedMaterial = _edgesMat;

        _verticesMat = Vertices[0].GetComponent<MeshRenderer>().material;
        for (int i = 0; i < Vertices.Length; i++)
            Vertices[i].GetComponent<MeshRenderer>().sharedMaterial = _verticesMat;

        _facesMat = Faces[0].GetComponent<MeshRenderer>().material;
        for (int i = 0; i < Faces.Length; i++)
            Faces[i].GetComponent<MeshRenderer>().sharedMaterial = _facesMat;

        // RULE SEED
        var rnd = RuleSeedable.GetRNG();
        var faceDimensions = new[] { 3, 1, 2, 0 };
        _faces = new List<bool?[]>();

        for (var i = 0; i < faceDimensions.Length; i++)
            for (var j = i + 1; j < faceDimensions.Length; j++)
            {
                var which = rnd.Next(0, 2) != 0;
                if (rnd.Next(0, 2) == 0)
                {
                    _faces.Add(Enumerable.Range(0, 4).Select(d => d == faceDimensions[i] ? false : d == faceDimensions[j] ? which : (bool?) null).ToArray());
                    _faces.Add(Enumerable.Range(0, 4).Select(d => d == faceDimensions[i] ? true : d == faceDimensions[j] ? which : (bool?) null).ToArray());
                }
                else
                {
                    _faces.Add(Enumerable.Range(0, 4).Select(d => d == faceDimensions[i] ? which : d == faceDimensions[j] ? false : (bool?) null).ToArray());
                    _faces.Add(Enumerable.Range(0, 4).Select(d => d == faceDimensions[i] ? which : d == faceDimensions[j] ? true : (bool?) null).ToArray());
                }
            }
        rnd.ShuffleFisherYates(_faces);
        _colorPermutations = rnd.ShuffleFisherYates(
            new[] { "RYGB", "RYBG", "RGYB", "RGBY", "RBYG", "RBGY", "YRGB", "YRBG", "YGRB", "YGBR", "YBRG", "YBGR", "GRYB", "GRBY", "GYRB", "GYBR", "GBRY", "GBYR", "BRYG", "BRGY", "BYRG", "BYGR", "BGRY", "BGYR" }
                .Select(str => str.Select(ch => "RYGB".IndexOf(ch)).ToArray()).ToArray()
        ).Take(12).ToArray();

        // GENERATE PUZZLE
        _rotations = new int[5];
        for (int i = 0; i < _rotations.Length; i++)
        {
            var axes = "XYZW".ToArray().Shuffle();
            _rotations[i] = Array.IndexOf(_rotationNames, string.Format("{0}{1}", axes[0], axes[1]));
        }
        Debug.LogFormat(@"[The Hypercube #{0}] Rotations are: {1}", _moduleId, _rotations.Select(rot => _rotationNames[rot]).Join(", "));

        for (var i = 0; i < 16; i++)
            Vertices[i].OnInteract = VertexClick(i);

        _rotationCoroutine = StartCoroutine(RotateHypercube());
    }

    private KMSelectable.OnInteractHandler VertexClick(int v)
    {
        return delegate
        {
            Vertices[v].AddInteractionPunch(.2f);
            if (_transitioning)
                return false;

            if (_rotationCoroutine != null)
            {
                _progress = 0;
                StartCoroutine(ColorChange(setVertexColors: true));
            }
            else if (v == _correctVertex)
            {
                _progress++;
                if (_progress == 4)
                {
                    Debug.LogFormat(@"[The Hypercube #{0}] Module solved.", _moduleId);
                    Module.HandlePass();
                    StartCoroutine(ColorChange(keepGrey: true));
                    Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
                }
                else
                {
                    StartCoroutine(ColorChange(setVertexColors: true));
                }
            }
            else
            {
                Debug.LogFormat(@"[The Hypercube #{0}] Incorrect vertex {1} pressed; resuming rotations.", _moduleId, StringifyShape(v));
                Module.HandleStrike();
                _rotationCoroutine = StartCoroutine(RotateHypercube(delay: true));
            }
            return false;
        };
    }

    private static readonly int[] _shapeOrder = { 3, 1, 2, 0 };
    private string StringifyShape(bool?[] shape)
    {
        var strs = _shapeOrder.Select(d => shape[d] == null ? null : _dimensionNames[d][shape[d].Value ? 1 : 0]).Where(s => s != null).ToArray();
        if (strs.Length == 0)
            return "hypercube";
        return strs.Join("-") + " " + (
            strs.Length == 1 ? "cube" :
            strs.Length == 2 ? "face" :
            strs.Length == 3 ? "edge" : "vertex");
    }
    private string StringifyShape(int vertex)
    {
        return StringifyShape(Enumerable.Range(0, 4).Select(d => (bool?) ((vertex & (1 << d)) != 0)).ToArray());
    }

    private IEnumerator ColorChange(bool keepGrey = false, bool setVertexColors = false, bool delay = false)
    {
        _transitioning = true;
        for (int i = 0; i < Vertices.Length; i++)
            Vertices[i].GetComponent<MeshRenderer>().sharedMaterial = _verticesMat;

        var prevHue = .5f;
        var prevSat = 0f;
        var prevV = .5f;
        SetColor(prevHue, prevSat, prevV);

        if (keepGrey)
            yield break;

        yield return new WaitForSeconds(delay ? 2.22f : .22f);

        _hue = Rnd.Range(0f, 1f);
        _sat = Rnd.Range(.6f, .9f);
        _v = Rnd.Range(.75f, 1f);

        var duration = 1.5f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            SetColor(Mathf.Lerp(prevHue, _hue, elapsed / duration), Mathf.Lerp(prevSat, _sat, elapsed / duration), Mathf.Lerp(prevV, _v, elapsed / duration));
            yield return null;
            elapsed += Time.deltaTime;
        }
        SetColor(_hue, _sat, _v);

        if (setVertexColors)
        {
            yield return new WaitUntil(() => _rotationCoroutine == null);
            PlayRandomSound();

            var desiredFace = _faces[_rotations[_progress]];
            var initialColors = Enumerable.Range(0, 4).ToList();
            var q = new Queue<int>();
            var colors = new int?[16];

            Debug.LogFormat(@"[The Hypercube #{0}] Stage {1} correct face: {2}", _moduleId, _progress + 1, StringifyShape(desiredFace));
            Debug.LogFormat(@"[The Hypercube #{0}] Stage {1} correct color: {2}", _moduleId, _progress + 1, _colorNames[_colorPermutations[_rotations[4]][_progress]]);

            // Assign the four colors on the desired face
            for (int v = 0; v < 1 << 4; v++)
            {
                if (Enumerable.Range(0, 4).All(d => desiredFace[d] == null || ((v & (1 << d)) != 0) == desiredFace[d].Value))
                {
                    var ix = Rnd.Range(0, initialColors.Count);
                    colors[v] = initialColors[ix];
                    initialColors.RemoveAt(ix);
                    for (var d = 0; d < 4; d++)
                        q.Enqueue(v ^ (1 << d));

                    if (colors[v].Value == _colorPermutations[_rotations[4]][_progress])
                    {
                        _correctVertex = v;
                        Debug.LogFormat(@"[The Hypercube #{0}] Stage {1} correct vertex: {2}", _moduleId, _progress + 1, StringifyShape(_correctVertex));
                    }
                }
            }

            // Assign the remaining colors as best as possible
            while (q.Count > 0)
            {
                var vx = q.Dequeue();
                if (colors[vx] != null)
                    continue;

                // For each color, determine how many faces would have a clash
                var numClashesPerColor = new int[4];
                for (var color = 0; color < 4; color++)
                    for (var d = 0; d < 4; d++)
                        for (var e = d + 1; e < 4; e++)
                            if (Enumerable.Range(0, 1 << 4).Any(v => (v & (1 << d)) == (vx & (1 << d)) && (v & (1 << e)) == (vx & (1 << e)) && colors[v] == color))
                                numClashesPerColor[color]++;

                var cs = Enumerable.Range(0, 4).ToArray();
                Array.Sort(numClashesPerColor, cs);
                colors[vx] = cs[0];

                // Little hack: just re-use that array to get shuffled numbers 0–3
                cs.Shuffle();
                for (var d = 0; d < 4; d++)
                    q.Enqueue(vx ^ (1 << cs[d]));
            }

            var goodFaces = 0;
            for (var d = 0; d < 4; d++)
                for (var dv = 0; dv < 2; dv++)
                    for (var e = d + 1; e < 4; e++)
                        for (int ev = 0; ev < 2; ev++)
                        {
                            var vertices = Enumerable.Range(0, 1 << 4).Where(v => ((v & (1 << d)) != 0) == (dv != 0) && ((v & (1 << e)) != 0) == (ev != 0)).Select(v => colors[v].Value).Distinct().ToArray();
                            if (vertices.Length == 4)
                                goodFaces++;
                        }

            _vertexColors = colors.Select(v => v.Value).ToArray();
            for (int v = 0; v < 1 << 4; v++)
                Vertices[v].GetComponent<MeshRenderer>().material.color = _vertexColorValues[_vertexColors[v]];
        }

        _transitioning = false;
    }

    private void PlayRandomSound()
    {
        Audio.PlaySoundAtTransform("Bleep" + Rnd.Range(1, 11), transform);
    }

    private void SetColor(float h, float s, float v)
    {
        _edgesMat.color = Color.HSVToRGB(h, s, v);
        _verticesMat.color = Color.HSVToRGB(h, s * .8f, v * .5f);
        var clr = Color.HSVToRGB(h, s * .8f, v * .75f);
        clr.a = .3f;
        _facesMat.color = clr;
    }

    private IEnumerator RotateHypercube(bool delay = false)
    {
        var colorChange = ColorChange(delay: delay);
        while (colorChange.MoveNext())
            yield return colorChange.Current;

        var unrotatedVertices = Enumerable.Range(0, 1 << 4).Select(i => new Point4D((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1, (i & 8) != 0 ? 1 : -1)).ToArray();
        SetHypercube(unrotatedVertices.Select(v => v.Project()).ToArray());

        while (!_transitioning)
        {
            yield return new WaitForSeconds(Rnd.Range(1.75f, 2.25f));

            for (int rot = 0; rot < _rotations.Length && !_transitioning; rot++)
            {
                var axis1 = "XYZW".IndexOf(_rotationNames[_rotations[rot]][0]);
                var axis2 = "XYZW".IndexOf(_rotationNames[_rotations[rot]][1]);
                var duration = 2f;
                var elapsed = 0f;

                while (elapsed < duration)
                {
                    var angle = easeInOutQuad(elapsed, 0, Mathf.PI / 2, duration);
                    var matrix = new double[16];
                    for (int i = 0; i < 4; i++)
                        for (int j = 0; j < 4; j++)
                            matrix[i + 4 * j] =
                                i == axis1 && j == axis1 ? Mathf.Cos(angle) :
                                i == axis1 && j == axis2 ? Mathf.Sin(angle) :
                                i == axis2 && j == axis1 ? -Mathf.Sin(angle) :
                                i == axis2 && j == axis2 ? Mathf.Cos(angle) :
                                i == j ? 1 : 0;

                    SetHypercube(unrotatedVertices.Select(v => (v * matrix).Project()).ToArray());

                    yield return null;
                    elapsed += Time.deltaTime;
                }

                // Reset the position of the hypercube
                SetHypercube(unrotatedVertices.Select(v => v.Project()).ToArray());
                yield return new WaitForSeconds(Rnd.Range(.5f, .6f));
            }
        }

        _transitioning = false;
        _rotationCoroutine = null;
    }

    private static float easeInOutQuad(float t, float start, float end, float duration)
    {
        var change = end - start;
        t /= duration / 2;
        if (t < 1)
            return change / 2 * t * t + start;
        t--;
        return -change / 2 * (t * (t - 2) - 1) + start;
    }

    private void SetHypercube(Vector3[] vertices)
    {
        // VERTICES
        for (int i = 0; i < 1 << 4; i++)
            Vertices[i].transform.localPosition = vertices[i];

        // EDGES
        var e = 0;
        for (int i = 0; i < 1 << 4; i++)
            for (int j = i + 1; j < 1 << 4; j++)
                if (((i ^ j) & ((i ^ j) - 1)) == 0)
                {
                    Edges[e].localPosition = (vertices[i] + vertices[j]) / 2;
                    Edges[e].localRotation = Quaternion.FromToRotation(Vector3.up, vertices[j] - vertices[i]);
                    Edges[e].localScale = new Vector3(.1f, (vertices[j] - vertices[i]).magnitude / 2, .1f);
                    e++;
                }

        foreach (var mesh in _generatedMeshes)
            Destroy(mesh);
        _generatedMeshes.Clear();

        // FACES
        var f = 0;
        for (int i = 0; i < 1 << 4; i++)
            for (int j = i + 1; j < 1 << 4; j++)
            {
                var b1 = i ^ j;
                var b2 = b1 & (b1 - 1);
                if (b2 != 0 && (b2 & (b2 - 1)) == 0 && (i & b1 & ((i & b1) - 1)) == 0 && (j & b1 & ((j & b1) - 1)) == 0)
                {
                    var mesh = new Mesh { vertices = new[] { vertices[i], vertices[i | j], vertices[i & j], vertices[j] }, triangles = new[] { 0, 1, 2, 1, 2, 3, 2, 1, 0, 3, 2, 1 } };
                    _generatedMeshes.Add(mesh);
                    Faces[f].sharedMesh = mesh;
                    f++;
                }
            }
    }

    private KMSelectable[] ProcessTwitchCommand(string command)
    {
        return null;
    }
}
