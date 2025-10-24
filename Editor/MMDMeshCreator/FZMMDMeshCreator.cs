using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using EUI = FZTools.EditorUtils.UI;
using ELayout = FZTools.EditorUtils.Layout;
using static FZTools.FZToolsConstants;

namespace FZTools
{
    public class FZMMDMeshCreator : EditorWindow
    {
        // referenced by https://site.nicovideo.jp/ch/userblomaga_thanks/archive/ar1471249
        static readonly List<string> morphNames = new List<string>(){
            "真面目","困る","にこり","怒り","上","下",// eyeblow
            "まばたき","笑い","ウィンク","ウィンク２",//eye
            "ウィンク右","ｳｨﾝｸ２右","はぅ","なごみ","びっくり",
            "じと目","なぬ！","瞳小","白目","瞳大",
            "あ","い","う","え","お","▲","∧","ω",// mouth
            "ω□","はんっ！","えー","にやり","ぺろっ",
            "口角上げ", "口角下げ",
            "頬染め","青ざめ"// other
        };

        [SerializeField]
        private GameObject avatar;

        string TargetAvatarName => avatar?.name;
        string MMDMeshOutputPath => $"{AssetUtils.OutputRootPath(TargetAvatarName)}/MMD";
        SkinnedMeshRenderer FaceMeshRenderer => avatar.GetComponent<VRCAvatarDescriptor>().GetVRCAvatarFaceMeshRenderer();
        int PreviewSize => (int)Math.Round(position.size.x / 2.3);
        string[] BlendShapes => Enumerable.Range(0, FaceMeshRenderer.sharedMesh.blendShapeCount)
                                    .Select(i => FaceMeshRenderer.sharedMesh.GetBlendShapeName(i))
                                    .ToArray();
        bool CanCreate => SelectedBlendShapeIndexes.Length == SelectedBlendShapeIndexes.Count(i => i >= 0);

        int[] SelectedBlendShapeIndexes = new int[morphNames.Count];
        Vector2 totalScrollPos;
        Vector2 shapesScrollPos;
        FZPreviewRenderer previewRenderer;
        int previewIndex = 0;
        bool forceCreate = false;


        [MenuItem("FZTools/MMDMeshCreator")]
        private static void OpenWindow()
        {
            var window = GetWindow<FZMMDMeshCreator>();
            window.titleContent = new GUIContent("MMDMeshCreator");
        }

        private void OnGUI()
        {
            ELayout.Scroll(ref totalScrollPos, () =>
            {
                ELayout.Horizontal(() =>
                {
                    EUI.Space();
                    ELayout.Vertical(() =>
                    {
                        BaseUI();
                        if (avatar == null)
                        {
                            return;
                        }

                        ELayout.Horizontal(() =>
                        {
                            // 右カラム
                            RightColumn();
                            // 左カラム
                            LeftColumn();
                        });

                        EUI.Space(2);
                        if (!CanCreate)
                        {
                            var unsetShape = string.Join(", ", SelectedBlendShapeIndexes.Select((v, i) => v < 0 ? morphNames[i] : null).Where(s => !s.isNullOrEmpty()));
                            EUI.ErrorBox($"未設定のシェイプ\n{unsetShape}");
                            EUI.Space();
                        }
                        EUI.ToggleWithLabel(ref forceCreate, "警告を無視して作成する");
                        // 作成ボタン
                        EUI.DisableGroup(!(CanCreate || forceCreate), () =>
                        {
                            EUI.Button("作成", CreateMMDMesh);
                        });
                        EUI.Space();
                    });
                    EUI.Space();
                });
            });
        }

        private void BaseUI()
        {
            EUI.Space(2);
            EUI.Label("Target Avatar");
            EUI.Space();
            EUI.ChangeCheck(() =>
            {
                EUI.ObjectField<GameObject>(ref avatar);
            }, () =>
            {
                ResetParams();
            });
            EUI.Space(2);
            EUI.InfoBox("MMD用シェイプを追加したFaceメッシュの作成を行えます\n同じ名前・近い名前のシェイプをある程度自動で設定しています");
            EUI.Space(2);
        }

        private void RightColumn()
        {
            ELayout.Vertical(() =>
            {
                if (SelectedBlendShapeIndexes != null && SelectedBlendShapeIndexes.Length > previewIndex)
                {
                    // プレビューサムネイル
                    Preview(PreviewSize, SelectedBlendShapeIndexes[previewIndex]);

                    InitialSetMMDShape();

                    // プルダウン
                    int index = SelectedBlendShapeIndexes[previewIndex];
                    int prevSelection = SelectedBlendShapeIndexes[previewIndex];
                    EUI.Popup(ref index, BlendShapes, GUILayout.Width(PreviewSize));
                    SelectedBlendShapeIndexes[previewIndex] = index;
                    if (prevSelection != index)
                    {
                        Repaint();
                    }
                }
                EUI.Space(3);
                EUI.CustomBox(() =>
                {
                    EUI.MiniLabel("まゆのシェイプ \n真面目, 困る, にこり, 怒り, 上, 下", GUILayout.Width(PreviewSize));
                    EUI.MiniLabel("目のシェイプ\nまばたき, 笑い, ウィンク, ウィンク２, ウィンク右, ｳｨﾝｸ２右, はぅ, なごみ, びっくり, じと目, なぬ！, 瞳小, 白目, 瞳大", GUILayout.Width(PreviewSize));
                    EUI.MiniLabel("口のシェイプ\nあ, い, う, え, お, ▲, ∧, ω, ω□, はんっ！, えー, にやり, ぺろっ, 口角上げ, 口角下げ", GUILayout.Width(PreviewSize));
                    EUI.MiniLabel("その他のシェイプ\n頬染め, 青ざめ", GUILayout.Width(PreviewSize));
                }, GUILayout.Width(PreviewSize));
            });
        }

        private void LeftColumn()
        {
            ELayout.Scroll(ref shapesScrollPos, () =>
            {
                var prevPreviewIndex = previewIndex;
                EUI.BoxRadioButton(ref previewIndex, morphNames.ToArray(), GUILayout.ExpandWidth(true));
                if (prevPreviewIndex != previewIndex)
                {
                    Repaint();
                }
            }, width: PreviewSize);
        }

        private void Preview(int previewSize, int shapeIndex)
        {
            if (previewRenderer == null)
            {
                previewRenderer = new FZPreviewRenderer(Instantiate(avatar));
            }
            var headBone = avatar.GetBoneRootObject().GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.ToLower().Contains("head"));
            var headPosition = headBone.position + new Vector3(0, headBone.position.y * 0.04f, 1 * avatar.transform.localScale.z * headBone.localScale.z);
            previewRenderer.SetCameraPosition(headPosition);

            previewRenderer.RenderPreview(previewSize, previewSize);

            var faceMesh = previewRenderer.GetPreviewObjectComponent<VRCAvatarDescriptor>().GetVRCAvatarFaceMeshRenderer();
            for (int j = 0; j < faceMesh.sharedMesh.blendShapeCount; j++)
            {
                faceMesh.SetBlendShapeWeight(j, 0);
            }
            if (shapeIndex >= 0)
            {
                previewRenderer.GetPreviewObjectComponent<VRCAvatarDescriptor>().GetVRCAvatarFaceMeshRenderer().SetBlendShapeWeight(shapeIndex, 100);
            }

            EditorGUILayout.LabelField(new GUIContent(previewRenderer.renderTexture), GUILayout.Width(previewSize), GUILayout.Height(previewSize));
            Repaint();
        }

        private void ResetParams()
        {
            SelectedBlendShapeIndexes = new int[morphNames.Count];
            SelectedBlendShapeIndexes = SelectedBlendShapeIndexes.Select(_ => -1).ToArray();
            if (previewRenderer != null)
            {
                previewIndex = 0;
                previewRenderer.EndPreview();
                previewRenderer = null;
            }

            Repaint();
        }

        private void InitialSetMMDShape()
        {
            for (int i = 0; i < morphNames.Count; i++)
            {
                // 完全に一致する場合はそのままSet
                var equalsShape = BlendShapes.FirstOrDefault(b => b.Equals(morphNames[i]));
                if (!equalsShape.isNullOrEmpty())
                {
                    if (SelectedBlendShapeIndexes[i] < 0)
                    {
                        var initialShape = equalsShape;
                        SelectedBlendShapeIndexes[i] = BlendShapes.ToList().IndexOf(initialShape);
                    }
                }
                else
                {
                    var eyebrowShapes = new List<string>() { "真面目", "困る", "にこり", "怒り", "上", "下" };
                    var eyebrowPrefixes = new List<string>() { "eyeblow", "eyebrow", "眉", "mayu", "brow" };

                    var eyeShapes = new List<string>(){
                        "まばたき","笑い","ウィンク","ウィンク２",
                        "ウィンク右","ｳｨﾝｸ２右","はぅ","なごみ","びっくり",
                        "じと目","なぬ！","瞳小","白目","瞳大"
                    };
                    var eyePrefixes = new List<string>() { "eye", "目" };

                    var mouthShapes = new List<string>(){
                        "あ","い","う","え","お","▲","∧","ω",
                        "ω□","はんっ！","えー","にやり","ぺろっ",
                        "口角上げ", "口角下げ"
                    };
                    var mouthPrefixes = new List<string>() { "mouth", "口" };

                    var otherShapes = new List<string>(){
                        "頬染め","青ざめ"
                    };

                    var isEyebrowMorph = eyebrowShapes.Contains(morphNames[i]);
                    var isEyeMorph = eyeShapes.Contains(morphNames[i]);
                    var isMouthMorph = mouthShapes.Contains(morphNames[i]);
                    var isOtherMorph = otherShapes.Contains(morphNames[i]);

                    var initialShape = "";

                    if (isEyebrowMorph)
                    {
                        var filteredShape = BlendShapes.Where(b => eyebrowPrefixes.Select(p => b.ToLower().Contains(p)).Any(p => p));
                        initialShape = filteredShape.FirstOrDefault(b => b.Contains(morphNames[i])) ?? "";
                        if (initialShape.isEmpty())
                        {
                            if (morphNames[i].Equals("怒り"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "怒" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                        }
                    }
                    if (isEyeMorph)
                    {
                        var filteredShape = BlendShapes.Where(b => eyePrefixes.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                    .Where(b => !eyebrowPrefixes.Select(p => b.ToLower().Contains(p)).Any(p => p));
                        initialShape = filteredShape.FirstOrDefault(b => b.Contains(morphNames[i])) ?? "";
                        if (initialShape.isEmpty())
                        {
                            if (morphNames[i].Equals("はぅ"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { ">", "＞" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("びっくり"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "驚", "おどろ", "ビックリ", "ﾋﾞｯｸﾘ" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("なごみ"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "和み", "棒目", "はにゃ", "シケ" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("ウィンク"))
                            {
                                initialShape = filteredShape.Where(b => (b.Contains("ウィンク") || b.Contains("ウインク")) && !(b.ToLower().Contains("r") || b.ToLower().Contains("右")))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("ウィンク２"))
                            {
                                initialShape = filteredShape.Where(b => (b.Contains("ウィンク") || b.Contains("ウインク")) && !(b.ToLower().Contains("r") || b.ToLower().Contains("右")))
                                                            .Where(b => (b.ToLower().Contains("2") || b.ToLower().Contains("２")))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("ウィンク右"))
                            {
                                initialShape = filteredShape.Where(b => (b.Contains("ウィンク") || b.Contains("ウインク")))
                                                            .Where(b => !(b.ToLower().Contains("2") || b.ToLower().Contains("２")) && (b.ToLower().Contains("r") || b.ToLower().Contains("右")))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("ｳｨﾝｸ２右"))
                            {
                                initialShape = filteredShape.Where(b => (b.Contains("ウィンク") || b.Contains("ウインク")))
                                                            .Where(b => (b.ToLower().Contains("2") || b.ToLower().Contains("２")) && (b.ToLower().Contains("r") || b.ToLower().Contains("右")))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("じと目"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "ジト", "ｼﾞﾄ", "半目" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("まばたき"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "瞑る", "閉じ", "blink" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("瞳大"))
                            {
                                initialShape = filteredShape.Where(b => b.ToLower().Contains("瞳") && b.ToLower().Contains("大"))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("瞳小") || morphNames[i].Equals("なぬ！"))
                            {
                                initialShape = filteredShape.Where(b => b.ToLower().Contains("瞳") && b.ToLower().Contains("小"))
                                                            .FirstOrDefault() ?? "";
                            }
                        }
                    }
                    if (isMouthMorph)
                    {
                        var filteredShape = BlendShapes.Where(b => mouthPrefixes.Select(p => b.ToLower().Contains(p)).Any(p => p));
                        initialShape = filteredShape.FirstOrDefault(b => b.Contains(morphNames[i])) ?? "";

                        if (initialShape.isEmpty())
                        {
                            if (morphNames[i].Equals("∧"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "^", "＾" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("▲"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "△", "三角" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("はんっ！"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "引き攣る", "ひきつる" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("にやり"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "ニヤリ", "ﾆﾔﾘ", "にっこり", "にこり" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                            if (morphNames[i].Equals("ぺろっ"))
                            {
                                initialShape = filteredShape.Where(b => new List<string>() { "舌出し", "ぺろ", "舌" }.Select(p => b.ToLower().Contains(p)).Any(p => p))
                                                            .FirstOrDefault() ?? "";
                            }
                        }
                    }
                    if (isOtherMorph)
                    {
                        initialShape = BlendShapes.FirstOrDefault(b => b.Contains(morphNames[i])) ?? "";
                    }

                    if (SelectedBlendShapeIndexes[i] < 0)
                    {
                        SelectedBlendShapeIndexes[i] = BlendShapes.ToList().IndexOf(initialShape);
                    }
                }
            }
        }

        private void CreateMMDMesh()
        {
            var mesh = Instantiate(FaceMeshRenderer.sharedMesh);

            // MMD用Blendshapeを追加する
            for (int i = 0; i < morphNames.Count; i++)
            {
                if (SelectedBlendShapeIndexes[i] < 0) continue;
                if (Enumerable.Range(0, mesh.blendShapeCount).Select(index => mesh.GetBlendShapeName(index)).Any(n => n == morphNames[i])) continue;

                var deltaVertices = new Vector3[mesh.vertices.Count()];
                var deltaNormals = new Vector3[mesh.normals.Count()];
                var deltaTangents = new Vector3[mesh.tangents.Count()];
                mesh.GetBlendShapeFrameVertices(SelectedBlendShapeIndexes[i], 0, deltaVertices, deltaNormals, deltaTangents);
                mesh.AddBlendShapeFrame(morphNames[i], 100, deltaVertices, deltaNormals, deltaTangents);
            }

            AssetUtils.DeleteAndCreateDirectoryRecursive(MMDMeshOutputPath);
            AssetUtils.CreateAsset(mesh, $"{MMDMeshOutputPath}/{mesh.name.Replace("(Clone)", "")}_MMD.mesh");

            ResetParams();
        }
    }
}