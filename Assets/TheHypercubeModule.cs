﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    public MeshFilter Faces;
    public Mesh Quad;
    public TextMesh RotationText;
    public GameObject[] ProgressLights;

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

    // Long-press handling
    private bool _isButtonDown;
    private Coroutine _buttonDownCoroutine;

    private Material _edgesMat, _verticesMat, _facesMat;
    private Mesh _lastFacesMesh = null;
    private static readonly string[] _rotationNames = new[] { "XY", "YX", "XZ", "ZX", "XW", "WX", "YZ", "ZY", "YW", "WY", "ZW", "WZ" };
    private static readonly string[][] _dimensionNames = new[] { new[] { "left", "right" }, new[] { "bottom", "top" }, new[] { "front", "back" }, new[] { "zig", "zag" } };
    private static readonly string[] _colorNames = new[] { "red", "yellow", "green", "blue" };
    private static readonly Color[] _vertexColorValues = "e54747,e5e347,47e547,3ba0f1".Split(',').Select(str => new Color(Convert.ToInt32(str.Substring(0, 2), 16) / 255f, Convert.ToInt32(str.Substring(2, 2), 16) / 255f, Convert.ToInt32(str.Substring(4, 2), 16) / 255f)).ToArray();
    private static readonly int[] _shapeOrder = { 3, 1, 2, 0 };

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        _edgesMat = Edges[0].GetComponent<MeshRenderer>().material;
        for (int i = 0; i < Edges.Length; i++)
            Edges[i].GetComponent<MeshRenderer>().sharedMaterial = _edgesMat;

        _verticesMat = Vertices[0].GetComponent<MeshRenderer>().material;
        for (int i = 0; i < Vertices.Length; i++)
            Vertices[i].GetComponent<MeshRenderer>().sharedMaterial = _verticesMat;

        _facesMat = Faces.GetComponent<MeshRenderer>().material;

        SetHypercube(GetUnrotatedVertices().Select(p => p.Project()).ToArray());

        // RULE SEED
        var rnd = RuleSeedable.GetRNG();
        Debug.LogFormat("[The Hypercube #{0}] Using rule seed: {1}", _moduleId, rnd.Seed);
        _faces = new List<bool?[]>();

        for (var i = 0; i < _shapeOrder.Length; i++)
            for (var j = i + 1; j < _shapeOrder.Length; j++)
            {
                var which = rnd.Next(0, 2) != 0;
                if (rnd.Next(0, 2) == 0)
                {
                    _faces.Add(Enumerable.Range(0, 4).Select(d => d == _shapeOrder[i] ? false : d == _shapeOrder[j] ? which : (bool?) null).ToArray());
                    _faces.Add(Enumerable.Range(0, 4).Select(d => d == _shapeOrder[i] ? true : d == _shapeOrder[j] ? which : (bool?) null).ToArray());
                }
                else
                {
                    _faces.Add(Enumerable.Range(0, 4).Select(d => d == _shapeOrder[i] ? which : d == _shapeOrder[j] ? false : (bool?) null).ToArray());
                    _faces.Add(Enumerable.Range(0, 4).Select(d => d == _shapeOrder[i] ? which : d == _shapeOrder[j] ? true : (bool?) null).ToArray());
                }
            }
        rnd.ShuffleFisherYates(_faces);
        _colorPermutations = rnd.ShuffleFisherYates(
            new[] { "RYGB", "RYBG", "RGYB", "RGBY", "RBYG", "RBGY", "YRGB", "YRBG", "YGRB", "YGBR", "YBRG", "YBGR", "GRYB", "GRBY", "GYRB", "GYBR", "GBRY", "GBYR", "BRYG", "BRGY", "BYRG", "BYGR", "BGRY", "BGYR" }
                .Select(str => str.Select(ch => "RYGB".IndexOf(ch)).ToArray()).ToArray()
        ).Take(12).ToArray();

        Debug.LogFormat("<The Hypercube #{0}> Rules:\n{1}", _moduleId, Enumerable.Range(0, _rotationNames.Length).Select(ix => string.Format("{0}={1}", _rotationNames[ix], StringifyShape(_faces[ix]))).Join("\n"));

        // GENERATE PUZZLE
        _rotations = new int[5];
        for (int i = 0; i < _rotations.Length; i++)
        {
            var axes = "XYZW".ToArray().Shuffle();
            _rotations[i] = Array.IndexOf(_rotationNames, string.Concat(axes[0], axes[1]));
        }

        // ## FOR CREATING THE “ALL ROTATIONS” GIF
        //if (_moduleId >= 1 && _moduleId <= 12)
        //{
        //    _rotations = new int[5] { _moduleId - 1, _moduleId - 1, _moduleId - 1, _moduleId - 1, _moduleId - 1 };
        //    RotationText.text = _rotationNames[_moduleId - 1];
        //    RotationText.gameObject.SetActive(true);
        //}
        //else
        //    RotationText.gameObject.SetActive(false);
        // ## END

        Debug.LogFormat(@"[The Hypercube #{0}] Rotations are: {1}", _moduleId, _rotations.Select(rot => _rotationNames[rot]).Join(", "));

        for (var i = 0; i < 1 << 4; i++)
        {
            Vertices[i].OnInteract = VertexClick(i);
            Vertices[i].OnInteractEnded = VertexRelease(i);
        }

        _rotationCoroutine = StartCoroutine(RotateHypercube());
    }

    private Point4D[] GetUnrotatedVertices()
    {
        return Enumerable.Range(0, 1 << 4).Select(i => new Point4D((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1, (i & 8) != 0 ? 1 : -1)).ToArray();
    }

    private KMSelectable.OnInteractHandler VertexClick(int v)
    {
        return delegate
        {
            Vertices[v].AddInteractionPunch(.2f);
            if (!_transitioning && _progress < 4)
                _buttonDownCoroutine = StartCoroutine(HandleLongPress(v));
            return false;
        };
    }

    private IEnumerator HandleLongPress(int v)
    {
        if (_transitioning || _progress == 4)
            yield break;

        _isButtonDown = true;
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Vertices[v].transform);

        yield return new WaitForSeconds(.7f);
        _isButtonDown = false;
        _buttonDownCoroutine = null;
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonRelease, Vertices[v].transform);

        // Handle long press
        if (_rotationCoroutine == null && _progress < 4)
        {
            _rotationCoroutine = StartCoroutine(RotateHypercube(delay: true));
            Debug.LogFormat("[The Hypercube #{0}] Module reset.", _moduleId);
        }
    }

    private Action VertexRelease(int v)
    {
        return delegate
        {
            if (!_isButtonDown || _progress == 4) // Long press already handled by HandleLogPress()
                return;
            _isButtonDown = false;

            if (_buttonDownCoroutine != null)
            {
                StopCoroutine(_buttonDownCoroutine);
                _buttonDownCoroutine = null;
            }

            // Handle short press
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
                    foreach (var light in ProgressLights)
                        light.SetActive(false);
                    Debug.LogFormat(@"[The Hypercube #{0}] Module solved.", _moduleId);
                    Module.HandlePass();
                    StartCoroutine(ColorChange(keepGrey: true));
                    Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
                }
                else
                {
                    ProgressLights[_progress - 1].SetActive(true);
                    StartCoroutine(ColorChange(setVertexColors: true));
                }
            }
            else
            {
                Debug.LogFormat(@"[The Hypercube #{0}] Incorrect vertex {1} pressed; resuming rotations.", _moduleId, StringifyShape(v));
                Module.HandleStrike();
                _rotationCoroutine = StartCoroutine(RotateHypercube(delay: true));
            }
        };
    }

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
        {
            _transitioning = false;
            yield break;
        }

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
            var colors = new int?[1 << 4];

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
        clr.a = .1f;
        _facesMat.color = clr;
    }

    private IEnumerator RotateHypercube(bool delay = false)
    {
        foreach (var light in ProgressLights)
            light.SetActive(false);

        var colorChange = ColorChange(delay: delay);
        while (colorChange.MoveNext())
            yield return colorChange.Current;

        var unrotatedVertices = GetUnrotatedVertices();
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
                    var angle = Easing.InOutQuad(elapsed, 0, Mathf.PI / 2, duration);
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

        _rotationCoroutine = null;
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

        // FACES
        if (_lastFacesMesh != null)
            Destroy(_lastFacesMesh);

        var f = 0;
        var triangles = new List<int>();
        for (int i = 0; i < 1 << 4; i++)
            for (int j = i + 1; j < 1 << 4; j++)
            {
                var b1 = i ^ j;
                var b2 = b1 & (b1 - 1);
                if (b2 != 0 && (b2 & (b2 - 1)) == 0 && (i & b1 & ((i & b1) - 1)) == 0 && (j & b1 & ((j & b1) - 1)) == 0)
                {
                    triangles.AddRange(new[] { i, i | j, i & j, i | j, i & j, j, i & j, i | j, i, j, i & j, i | j });
                    f++;
                }
            }
        _lastFacesMesh = new Mesh { vertices = vertices, triangles = triangles.ToArray() };
        _lastFacesMesh.RecalculateNormals();
        Faces.sharedMesh = _lastFacesMesh;
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} go [use when hypercube is rotating] | !{0} zig-bottom-front-left [presses a vertex when the hypercube is not rotating] | !{0} reset [forget input and resume rotations]";
#pragma warning restore 414

    IEnumerator ProcessTwitchCommand(string command)
    {
        if (_rotationCoroutine != null && Regex.IsMatch(command, @"^\s*(go|activate|stop|run|start|on|off)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            yield return new[] { Vertices[0] };
            yield break;
        }

        if (_rotationCoroutine == null && Regex.IsMatch(command, @"^\s*(reset|go back|return|resume|rotate|rotations|cancel|abort)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            Vertices[0].OnInteract();
            yield return new WaitForSeconds(1f);
            Vertices[0].OnInteractEnded();
            yield break;
        }

        Match m;
        if (_rotationCoroutine == null && (m = Regex.Match(command, string.Format(@"^\s*((?:{0})(?:[- ,;]*(?:{0}))*)\s*$", _dimensionNames.SelectMany(x => x).Join("|")), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            var elements = m.Groups[1].Value.Split(new[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (elements.Length != 4)
            {
                yield return "sendtochaterror It’s a 4D hypercube, you gotta have 4 dimensions.";
                yield break;
            }
            var dimensions = elements.Select(el => _dimensionNames.IndexOf(d => d.Any(dn => dn.EqualsIgnoreCase(el)))).ToArray();
            var invalid = Enumerable.Range(0, 3).SelectMany(i => Enumerable.Range(i + 1, 3 - i).Where(j => dimensions[i] == dimensions[j]).Select(j => new { i, j })).FirstOrDefault();
            if (invalid != null)
            {
                yield return elements[invalid.i].EqualsIgnoreCase(elements[invalid.j])
                    ? string.Format("sendtochaterror You wrote “{0}” twice.", elements[invalid.i], elements[invalid.j])
                    : string.Format("sendtochaterror “{0}” and “{1}” doesn’t jive.", elements[invalid.i], elements[invalid.j]);
                yield break;
            }
            var vertexIx = 0;
            for (int i = 0; i < 4; i++)
                vertexIx |= _dimensionNames[dimensions[i]].IndexOf(dn => dn.EqualsIgnoreCase(elements[i])) << dimensions[i];
            yield return null;
            yield return new[] { Vertices[vertexIx] };
        }
    }

    IEnumerator TwitchHandleForcedSolve()
    {
        if (_rotationCoroutine != null)
        {
            Vertices[0].OnInteract();
            yield return new WaitForSeconds(.1f);
            Vertices[0].OnInteractEnded();
            yield return new WaitForSeconds(.1f);
        }

        while (_progress < 4)
        {
            while (_transitioning)
                yield return true;
            yield return new WaitForSeconds(.1f);

            var correctVertex = _correctVertex;
            Vertices[correctVertex].OnInteract();
            yield return new WaitForSeconds(.1f);
            Vertices[correctVertex].OnInteractEnded();
            yield return new WaitForSeconds(.1f);
        }

        while (_transitioning)
            yield return true;
    }
}
