using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Casiwar
{
    /// <summary>
    /// Горячие клавиши (ПК):
    /// • 1–4 — вкладки «Город», «Наука», «Поле», «Войска»; Enter — «Конец хода» / «В БОЙ!»;
    /// • на поле: A — «Авто» вербовки, Q / W / E — чего в следующий ход больше упадёт в сбор (🪙 / ⚒ / 🍎);
    /// • на трофеях: Enter (или D) — сразу итог / «Дальше», Пробел — «Крутить», A — «Авто»;
    /// • в бою клавиш нет: бой идёт сам (только кнопка скорости).
    /// Кнопки находятся по их обработчикам (подходят и сцены старой версии); в углу каждой — маленькая подпись клавиши.
    /// Клавиша жмёт кнопку, только когда та видна и активна, поэтому одна буква работает на разных экранах по-своему.
    /// GameManager добавляет этот компонент сам.
    /// </summary>
    public class KeyboardShortcuts : MonoBehaviour
    {
        private sealed class Binding
        {
            public Button Button;
            public KeyCode[] Keys;
        }

        private static readonly Color HintBackground = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color HintText = new Color(1f, 1f, 1f, 0.9f);
        private const float HintHeight = 24f;
        private const float HintFontSize = 17f;

        private readonly List<Binding> bindings = new List<Binding>();

        private void Start()
        {
            Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Bind(buttons, typeof(GameManager), nameof(GameManager.ShowCityTab), "1", KeyCode.Alpha1, KeyCode.Keypad1);
            Bind(buttons, typeof(GameManager), nameof(GameManager.ShowTechTab), "2", KeyCode.Alpha2, KeyCode.Keypad2);
            Bind(buttons, typeof(GameManager), nameof(GameManager.ShowBoardTab), "3", KeyCode.Alpha3, KeyCode.Keypad3);
            Bind(buttons, typeof(GameManager), nameof(GameManager.ShowUnitsTab), "4", KeyCode.Alpha4, KeyCode.Keypad4);
            Bind(buttons, typeof(GameManager), nameof(GameManager.StartBattle), "Enter", KeyCode.Return, KeyCode.KeypadEnter);
            Bind(buttons, typeof(GameManager), nameof(GameManager.EndTurn), "Enter", KeyCode.Return, KeyCode.KeypadEnter);
            Bind(buttons, typeof(MatchBoard), nameof(MatchBoard.ToggleAutoPlay), "A", KeyCode.A);
            Bind(buttons, typeof(RecruitSlot), nameof(RecruitSlot.ToggleAuto), "A", KeyCode.A);
            Bind(buttons, typeof(MatchBoard), nameof(MatchBoard.ReshuffleOnce), "S", KeyCode.S);
            Bind(buttons, typeof(LootSlotMachine), nameof(LootSlotMachine.Spin), "Пробел", KeyCode.Space);
            Bind(buttons, typeof(LootSlotMachine), nameof(LootSlotMachine.ToggleAutoSpin), "A", KeyCode.A);
            Bind(buttons, typeof(GameManager), nameof(GameManager.FinishLoot), "Enter", KeyCode.Return, KeyCode.KeypadEnter, KeyCode.D);
            BindFocus(buttons);
            PutAutoBeforeReshuffle(buttons);
        }

        private void Update()
        {
            DropButtonSelection();
            if (!Input.anyKeyDown) return;
            foreach (Binding binding in bindings)
            {
                Button button = binding.Button;
                if (button == null || !button.isActiveAndEnabled || !button.interactable) continue;
                if (!binding.Keys.Any(Input.GetKeyDown)) continue;
                button.onClick.Invoke();
                return; // кнопка могла сменить экран — та же клавиша не должна тут же нажать кнопку на новом
            }
        }

        /// <summary>
        /// Нажатая мышью кнопка остаётся «выбранной», и Enter / Пробел нажали бы её ещё раз (вместо горячей клавиши) —
        /// поэтому выбор с кнопок сразу снимается.
        /// </summary>
        private static void DropButtonSelection()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;
            GameObject selected = events.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<Button>() != null) events.SetSelectedGameObject(null);
        }

        /// <summary>Кнопка, у которой в onClick стоит метод method компонента типа targetType.</summary>
        private static IEnumerable<Button> WithHandler(IEnumerable<Button> buttons, Type targetType, string method)
        {
            foreach (Button button in buttons)
            {
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    Object target = button.onClick.GetPersistentTarget(i);
                    if (target != null && targetType.IsInstanceOfType(target) && button.onClick.GetPersistentMethodName(i) == method)
                    {
                        yield return button;
                        break;
                    }
                }
            }
        }

        private void Bind(Button[] buttons, Type targetType, string method, string hint, params KeyCode[] keys)
        {
            foreach (Button button in WithHandler(buttons, targetType, method)) Add(button, hint, keys);
        }

        private void Add(Button button, string hint, KeyCode[] keys)
        {
            bindings.Add(new Binding { Button = button, Keys = keys });
            AddHint(button, hint);
        }

        /// <summary>Фокус на завтра: Q — золото, W — производство, E — еда (кнопки Btn_Focus_Gold / _Production / _Food).</summary>
        private void BindFocus(Button[] buttons)
        {
            List<Button> focus = WithHandler(buttons, typeof(CityGridManager), nameof(CityGridManager.CycleFocus)).ToList();
            (ResourceType resource, string hint, KeyCode key)[] keys =
            {
                (ResourceType.Gold, "Q", KeyCode.Q),
                (ResourceType.Production, "W", KeyCode.W),
                (ResourceType.Food, "E", KeyCode.E),
            };
            foreach (var (resource, hint, key) in keys)
            {
                Button button = focus.FirstOrDefault(b => b.name == $"Btn_Focus_{resource}");
                if (button != null) Add(button, hint, new[] { key });
            }
        }

        /// <summary>На поле «Авто» (A) стоит левее «Перемешать» (S) — как клавиши на клавиатуре (и в сценах старой версии).</summary>
        private static void PutAutoBeforeReshuffle(Button[] buttons)
        {
            Button auto = WithHandler(buttons, typeof(MatchBoard), nameof(MatchBoard.ToggleAutoPlay)).FirstOrDefault();
            Button reshuffle = WithHandler(buttons, typeof(MatchBoard), nameof(MatchBoard.ReshuffleOnce)).FirstOrDefault();
            if (auto == null || reshuffle == null || auto.transform.parent != reshuffle.transform.parent) return;
            int reshuffleIndex = reshuffle.transform.GetSiblingIndex();
            if (auto.transform.GetSiblingIndex() > reshuffleIndex) auto.transform.SetSiblingIndex(reshuffleIndex);
        }

        /// <summary>Маленькая подпись клавиши в правом верхнем углу кнопки.</summary>
        private static void AddHint(Button button, string hint)
        {
            if (button.transform.Find("KeyHint") != null) return;
            TMP_Text source = button.GetComponentInChildren<TMP_Text>(true);

            var chip = new GameObject("KeyHint", typeof(RectTransform)) { layer = button.gameObject.layer };
            var chipRect = (RectTransform)chip.transform;
            chipRect.SetParent(button.transform, false);
            chipRect.anchorMin = chipRect.anchorMax = chipRect.pivot = new Vector2(1f, 1f);
            chipRect.anchoredPosition = new Vector2(-5f, -5f);
            var background = chip.AddComponent<Image>();
            background.sprite = GameVisuals.RoundedRect;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 2.5f;
            background.color = HintBackground;
            background.raycastTarget = false;

            // Ширина плашки — по тексту (считается и у кнопок на скрытых экранах, когда те откроются)
            chipRect.sizeDelta = new Vector2(HintHeight, HintHeight);
            var layout = chip.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 0, 0);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            chip.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var labelObject = new GameObject("Key", typeof(RectTransform)) { layer = button.gameObject.layer };
            ((RectTransform)labelObject.transform).SetParent(chipRect, false);
            labelObject.AddComponent<LayoutElement>().minWidth = HintHeight - 12f;
            var label = labelObject.AddComponent<TextMeshProUGUI>();
            if (source != null) label.font = source.font;
            label.text = hint;
            label.fontSize = HintFontSize;
            label.fontStyle = FontStyles.Bold;
            label.color = HintText;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
        }
    }
}
