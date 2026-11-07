using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Unscaled, restrained hover/press feedback without rebuilding the layout.</summary>
    public sealed class UIHoverFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        Button button;
        Graphic graphic;
        RoundedPanel panel;
        Color idle, hover, idleBorder, hoverBorder;
        bool over, selected, pressed;
        float amount;
        Vector3 baseScale;

        public static UIHoverFeedback Attach(Button button, bool warmAccent = false)
        {
            var effect = button.GetComponent<UIHoverFeedback>() ?? button.gameObject.AddComponent<UIHoverFeedback>();
            effect.Configure(button,warmAccent);
            return effect;
        }
        public void Configure(Button source, bool warmAccent = false)
        {
            button=source; graphic=source.targetGraphic; panel=graphic as RoundedPanel;
            idle=graphic.color;hover=UIStyleConfig.Current.ButtonHoverFill;
            idleBorder=panel!=null?panel.BorderColor:UIStyleConfig.Current.PanelBorder;
            hoverBorder=warmAccent?UIStyleConfig.Current.AccentWarm:UIStyleConfig.Current.AccentCool;
            hoverBorder.a=.68f;baseScale=transform.localScale;
            button.transition=Selectable.Transition.None;
        }
        void Update()
        {
            if(graphic==null||button==null)return;
            float target=button.IsInteractable()&&(over||selected)?1:0;
            amount=Mathf.MoveTowards(amount,target,Time.unscaledDeltaTime/Mathf.Max(.01f,UIStyleConfig.Current.HoverDuration));
            float smooth=amount*amount*(3-2*amount);
            graphic.color=Color.Lerp(idle,hover,smooth);
            if(panel!=null)panel.SetBorder(Color.Lerp(idleBorder,hoverBorder,smooth),UIStyleConfig.Current.BorderWidth);
            transform.localScale=baseScale*(pressed&&button.IsInteractable()?.993f:Mathf.Lerp(1,1.012f,smooth));
        }
        void OnDisable()
        {
            over=selected=pressed=false;amount=0;
            if(graphic!=null)graphic.color=idle;
            if(panel!=null)panel.SetBorder(idleBorder,UIStyleConfig.Current.BorderWidth);
            if(baseScale!=Vector3.zero)transform.localScale=baseScale;
        }
        public void OnPointerEnter(PointerEventData e)=>over=true;
        public void OnPointerExit(PointerEventData e){over=false;pressed=false;}
        public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)pressed=true;}
        public void OnPointerUp(PointerEventData e)=>pressed=false;
        public void OnSelect(BaseEventData e)=>selected=true;
        public void OnDeselect(BaseEventData e){selected=false;pressed=false;}
    }
}
