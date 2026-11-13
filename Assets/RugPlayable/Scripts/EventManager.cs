using System;
using System.Collections.Generic;
using RugMaster;
using UnityEngine;

/// <summary>
/// EventManager - Quản lý sự kiện tập trung (Observer Pattern), cùng API với các game trước:
/// EventManager.AddListener&lt;T&gt;(handler), RemoveListener, Trigger(new T{...}).
/// </summary>
public static class EventManager
{
    private static readonly Dictionary<Type, Action<IGameEvent>> s_Events = new Dictionary<Type, Action<IGameEvent>>();
    private static readonly Dictionary<Delegate, Action<IGameEvent>> s_EventLookups = new Dictionary<Delegate, Action<IGameEvent>>();
    private static readonly object s_Lock = new object();

    /// <summary>Tự động reset static data khi nhấn Play trong Unity Editor.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ClearAll()
    {
        lock (s_Lock)
        {
            s_Events.Clear();
            s_EventLookups.Clear();
        }
    }

    public static void AddListener<T>(Action<T> listener) where T : IGameEvent
    {
        if (listener == null) return;
        lock (s_Lock)
        {
            if (s_EventLookups.ContainsKey(listener)) return;
            Action<IGameEvent> wrapper = e => listener((T)e);
            s_EventLookups[listener] = wrapper;
            s_Events[typeof(T)] = s_Events.TryGetValue(typeof(T), out var existing) ? existing + wrapper : wrapper;
        }
    }

    public static void RemoveListener<T>(Action<T> listener) where T : IGameEvent
    {
        if (listener == null) return;
        lock (s_Lock)
        {
            if (!s_EventLookups.TryGetValue(listener, out var wrapper)) return;
            if (s_Events.TryGetValue(typeof(T), out var existing))
            {
                existing -= wrapper;
                if (existing == null) s_Events.Remove(typeof(T));
                else s_Events[typeof(T)] = existing;
            }
            s_EventLookups.Remove(listener);
        }
    }

    public static void Trigger(IGameEvent evt)
    {
        if (evt == null) return;
        Action<IGameEvent> actionToInvoke;
        lock (s_Lock)
        {
            if (!s_Events.TryGetValue(evt.GetType(), out actionToInvoke)) return;
        }
        foreach (var d in actionToInvoke.GetInvocationList())
        {
            try { ((Action<IGameEvent>)d).Invoke(evt); }
            catch (Exception ex)
            {
                Debug.LogError($"[EventManager] Exception when triggering event of type {evt.GetType().Name} in listener {d.Method.Name}: {ex}");
            }
        }
    }

    public static bool HasListener<T>() where T : IGameEvent
    {
        lock (s_Lock) return s_Events.ContainsKey(typeof(T));
    }
}

/// <summary>Interface đánh dấu (Marker Interface) bắt buộc cho mọi Event.</summary>
public interface IGameEvent { }

// ===== Sự kiện của Rug Master =====

/// <summary>LevelManager vừa tạo RugSimulation mới (load, restart hoặc sang level kế).</summary>
public struct OnLevelLoadedEvent : IGameEvent
{
    public int level;
    public RugSimulation simulation;
}

/// <summary>Một ô thảm vừa được cắt.</summary>
public struct OnTrimEvent : IGameEvent { public Cell cell; }

/// <summary>Một sợi len vừa rơi vào giỏ.</summary>
public struct OnFiberLandedEvent : IGameEvent { public Fiber fiber; }

/// <summary>Một giỏ vừa đầy và bắt đầu đóng gói.</summary>
public struct OnBasketCompletedEvent : IGameEvent { public Basket basket; }

/// <summary>Level thắng (mọi sợi len đã vào giỏ).</summary>
public struct OnLevelWonEvent : IGameEvent { public int level; }

/// <summary>Kênh bị kẹt: không còn nước đi.</summary>
public struct OnLevelJammedEvent : IGameEvent { public int level; }

/// <summary>Thông báo ngắn cho người chơi (vd. booster không dùng được).</summary>
public struct OnShowMessage : IGameEvent { public string message; }

/// <summary>Người chơi bỏ cuộc sau khi kẹt (popup Level Failed mở, đã trừ 1 mạng).</summary>
public struct OnLevelFailedEvent : IGameEvent { public int level; }

/// <summary>Số coin vừa đổi (Economy.AddCoin / TrySpendCoin). delta âm = tiêu coin.</summary>
public struct OnCoinChangedEvent : IGameEvent { public int coinAmount; public int delta; public string reason; }

/// <summary>Bấm booster nhưng không có tác dụng (không trừ lượt): view hiện thông báo ngắn giải thích lý do.</summary>
public struct OnBoosterUnavailableEvent : IGameEvent { public bool sweep; public string message; }
