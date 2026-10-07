using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Casiwar
{
    public enum TechState
    {
        Researched,
        Available,
        Locked,
    }

    /// <summary>
    /// Карточка технологии в дереве науки. Префаб: Image (фон) + TechCardView, дочерние Title, Description
    /// (TextMeshPro) и Button с TextMeshPro внутри.
    /// </summary>
    public class TechCardView : MonoBehaviour
    {
        public Image background;
        public TMP_Text title;
        public TMP_Text description;
        public Button button;
        public TMP_Text buttonLabel;

        public Color researchedColor = new Color(0.20f, 0.36f, 0.24f);
        public Color availableColor = new Color(0.20f, 0.23f, 0.31f);
        public Color lockedColor = new Color(0.13f, 0.14f, 0.18f);

        public string TechId { get; private set; }

        public void Init(TechTreeView tree, string techId)
        {
            TechId = techId;
            name = $"Tech_{techId}";
            if (background == null) background = GetComponent<Image>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => tree.Research(techId));
            }
        }

        public void Bind(TechConfig tech, TechState state, bool affordable, string missing)
        {
            if (background != null)
            {
                background.color = state == TechState.Researched ? researchedColor
                    : state == TechState.Available ? availableColor : lockedColor;
            }
            if (title != null)
            {
                title.text = tech.title;
                title.color = state == TechState.Locked ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
            }
            if (description != null) description.text = tech.description;

            if (button != null) button.interactable = state == TechState.Available && affordable;
            if (buttonLabel != null)
            {
                switch (state)
                {
                    case TechState.Researched:
                        buttonLabel.text = "Изучено";
                        break;
                    case TechState.Available:
                        buttonLabel.text = $"Изучить · {tech.knowledgeCost} {GameVisuals.IconKnowledge}";
                        break;
                    default:
                        buttonLabel.text = $"Нужно: {missing}";
                        break;
                }
            }
        }
    }
}
